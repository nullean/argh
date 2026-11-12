using System.Text;
using System.Text.Json;
using Nullean.Argh.Help;
using Nullean.Make.Discovery;

namespace Nullean.Make.Execution;

/// <summary>How <c>--graph</c> draws the plan.</summary>
internal enum GraphFormat
{
	/// <summary>A tree in the terminal, with the order the targets run in below it.</summary>
	Tree,

	/// <summary>A Mermaid flowchart, for a pull request or the docs.</summary>
	Mermaid,

	/// <summary>The plan as JSON, for tools.</summary>
	Json
}

/// <summary>Reads <c>--graph</c>, <c>--graph mermaid</c> and <c>--graph=json</c> from the command line.</summary>
internal static class GraphOption
{
	public const string Name = "--graph";

	/// <summary>
	/// When <c>args[i]</c> is <c>--graph</c>, reads it and an optional format after it, advances <paramref name="i"/>
	/// past them and returns the format. A word after <c>--graph</c> that is not a format is left for the route.
	/// </summary>
	public static GraphFormat? TryRead(IReadOnlyList<string> args, ref int i)
	{
		var arg = args[i];
		if (arg.StartsWith(Name + "=", StringComparison.Ordinal))
		{
			i++;
			return Parse(arg[(Name.Length + 1)..]) ??
				throw new MakeException($"Unknown graph format '{arg[(Name.Length + 1)..]}'. Known: tree, mermaid, json.", 2);
		}
		if (arg != Name)
			return null;
		i++;
		if (i < args.Count && Parse(args[i]) is { } format)
		{
			i++;
			return format;
		}
		return GraphFormat.Tree;
	}

	private static GraphFormat? Parse(string value) => value.ToLowerInvariant() switch
	{
		"tree" => GraphFormat.Tree,
		"mermaid" => GraphFormat.Mermaid,
		"json" => GraphFormat.Json,
		_ => null
	};
}

/// <summary>Draws a <see cref="RunPlan"/> without running it.</summary>
internal static class PlanRenderer
{
	public static void Write(RunPlan plan, GraphFormat format, TextWriter output)
	{
		switch (format)
		{
			case GraphFormat.Mermaid:
				WriteMermaid(plan, output);
				break;
			case GraphFormat.Json:
				WriteJson(plan, output);
				break;
			default:
				WriteTree(plan, output);
				break;
		}
	}

	/// <summary>
	/// The plan for the targets nothing else depends on, each with its default value: what the whole build looks like.
	/// </summary>
	public static RunPlan WholeGraph(BuildGraph graph, ITargetSource source, PlanOptions options)
	{
		var referenced = graph.Targets.SelectMany(t => t.RequiresResolved.Concat(t.ComposesResolved)).ToHashSet();
		var roots = graph.Targets.Where(t => !t.Hidden && !referenced.Contains(t)).Select(t => new DepRef(t, null)).ToList();
		return RunPlan.Build(roots, source, options with { ChainRoots = false });
	}

	/// <summary>The targets whose bodies run, in order: what <c>Runs:</c> lists.</summary>
	public static IEnumerable<TargetInstance> Running(RunPlan plan) => plan.Instances.Where(i => i.Spec.Body is not null && i.Skipped is null);

	/// <summary><c>clean → build → test</c>.</summary>
	public static string RunOrder(RunPlan plan) => string.Join(" → ", Running(plan).Select(i => i.Name));

	/// <summary>
	/// Each instance's prerequisites without the ones another prerequisite already waits for, so a drawn graph has an
	/// edge per real ordering and no transitive ones.
	/// </summary>
	public static Dictionary<TargetInstance, List<TargetInstance>> DirectPrerequisites(RunPlan plan)
	{
		var ancestors = new Dictionary<TargetInstance, HashSet<TargetInstance>>();
		var direct = new Dictionary<TargetInstance, List<TargetInstance>>();
		foreach (var instance in plan.Instances)
		{
			var all = new HashSet<TargetInstance>();
			foreach (var prerequisite in instance.Prerequisites)
			{
				all.Add(prerequisite);
				all.UnionWith(ancestors[prerequisite]);
			}
			ancestors[instance] = all;
			var distinct = instance.Prerequisites.Distinct().ToList();
			direct[instance] = distinct.Where(p => !distinct.Any(q => q != p && ancestors[q].Contains(p))).ToList();
		}
		return direct;
	}

	// ── tree ──────────────────────────────────────────────────────────────────

	private sealed record Line(string Text, List<Line> Children);

	private static void WriteTree(RunPlan plan, TextWriter output)
	{
		var expanded = new HashSet<TargetInstance>();
		foreach (var root in plan.Roots)
		{
			var tree = Build(root, expanded);
			var description = root.Node.Description;
			output.WriteLine(string.IsNullOrEmpty(description) ? Accent(tree.Text) : $"{Accent(tree.Text)}  {description}");
			WriteChildren(tree.Children, "", output);
			output.WriteLine();
		}

		// Unrelated roots drawn side by side do not run in one sequence.
		if (!plan.Chained && plan.Roots.Count > 1)
			return;
		var order = RunOrder(plan);
		var cleanups = plan.Cleanups.Values.SelectMany(c => c).Distinct().Select(c => c.Name).ToList();
		if (cleanups.Count > 0)
			order += $", then cleanup: {string.Join(", ", cleanups)}";
		output.WriteLine($"{CliHelpFormatting.Section("Runs:")} {(order.Length == 0 ? "nothing" : order)}");

		foreach (var (instance, what) in plan.MissingPreconditions())
			output.WriteLine($"{CliHelpFormatting.Section("Missing:")} {what}, for {instance.Name}");
	}

	private static Line Build(TargetInstance instance, HashSet<TargetInstance> expanded)
	{
		var text = instance.Name + Annotation(instance.Skipped);
		if (!expanded.Add(instance))
			return new Line(text + Dim("  (above)"), []);
		if (instance.Spec.After.Count > 0)
			text += Dim($"  after {string.Join(", ", instance.Spec.After.Select(a => string.Join(" ", a.Node.Route)))}");
		var needs = instance.Spec.NeedsEnv.Concat(instance.Spec.NeedsTool).ToList();
		if (needs.Count > 0)
			text += Dim($"  needs {string.Join(", ", needs)}");

		var children = new List<Line>();
		foreach (var kind in new[] { StageKind.Requires, StageKind.Composes })
		{
			var lines = new List<Line>();
			foreach (var stage in instance.Stages.Where(s => s.Kind == kind))
			{
				var items = stage
					.Items
					.Select(item => item.Instance is null ? new Line(item.Name + Annotation(item.Skipped), []) : Build(item.Instance, expanded))
					.ToList();
				if (stage.Parallel)
					lines.Add(new Line(Dim("parallel"), items));
				else
					lines.AddRange(items);
			}
			if (lines.Count == 0)
				continue;

			// A target's dependencies hang off it directly; a command says which of its stages -s skips.
			if (instance.Node.Kind == TargetKind.Command)
				children.Add(new Line(Dim(kind == StageKind.Requires ? "requires" : "composes"), lines));
			else
				children.AddRange(lines);
		}
		if (instance.Spec.Cleanup.Count > 0)
			children.Add(new Line(Dim("cleanup, at the end"), [.. instance.Spec.Cleanup.Select(c => new Line(string.Join(" ", c.Node.Route), []))]));
		return new Line(text, children);
	}

	private static void WriteChildren(List<Line> children, string indent, TextWriter output)
	{
		for (var i = 0; i < children.Count; i++)
		{
			var last = i == children.Count - 1;
			output.WriteLine($"{indent}{(last ? "└─ " : "├─ ")}{children[i].Text}");
			WriteChildren(children[i].Children, indent + (last ? "   " : "│  "), output);
		}
	}

	private static string Annotation(SkipReason? reason) => reason switch
	{
		SkipReason.SingleTarget => Dim("  skipped: -s"),
		SkipReason.Condition => Dim("  skipped: its condition is false"),
		SkipReason.Excluded => Dim("  skipped: --skip"),
		_ => ""
	};

	private static string Accent(string text) => CliHelpFormatting.Accent(text);

	private static string Dim(string text) => CliHelpFormatting.DocRemarksLine(text);

	// ── mermaid ───────────────────────────────────────────────────────────────

	private static void WriteMermaid(RunPlan plan, TextWriter output)
	{
		var ids = new Dictionary<TargetInstance, string>();
		foreach (var instance in plan.Instances)
			ids[instance] = $"n{ids.Count}";

		var sb = new StringBuilder();
		var skipped = new List<string>();
		sb.AppendLine("flowchart TD");
		foreach (var instance in plan.Instances)
		{
			sb.AppendLine($"  {ids[instance]}[\"{Escape(instance.Name)}\"]");
			if (instance.Skipped is not null)
				skipped.Add(ids[instance]);
		}

		var extra = 0;
		var direct = DirectPrerequisites(plan);
		foreach (var instance in plan.Instances)
		{
			foreach (var prerequisite in direct[instance])
				sb.AppendLine($"  {ids[prerequisite]} --> {ids[instance]}");
			foreach (var item in instance.Stages.SelectMany(s => s.Items).Where(i => i.Instance is null))
			{
				var id = $"s{extra++}";
				sb.AppendLine($"  {id}[\"{Escape(item.Name)}\"]");
				sb.AppendLine($"  {id} -.-> {ids[instance]}");
				skipped.Add(id);
			}
		}

		if (skipped.Count > 0)
		{
			sb.AppendLine("  classDef skipped stroke-dasharray: 5 5,color:#888");
			sb.AppendLine($"  class {string.Join(",", skipped)} skipped");
		}
		output.Write(sb.ToString());
	}

	private static string Escape(string text) => text.Replace("\"", "#quot;");

	// ── json ──────────────────────────────────────────────────────────────────

	private static void WriteJson(RunPlan plan, TextWriter output)
	{
		using var stream = new MemoryStream();
		using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			json.WriteStartObject();
			json.WriteStartArray("roots");
			foreach (var root in plan.Roots)
				json.WriteStringValue(root.Name);
			json.WriteEndArray();

			json.WriteStartArray("targets");
			var direct = DirectPrerequisites(plan);
			foreach (var instance in plan.Instances)
			{
				json.WriteStartObject();
				json.WriteString("name", instance.Name);
				json.WriteStartArray("route");
				foreach (var segment in instance.Node.Route)
					json.WriteStringValue(segment);
				json.WriteEndArray();
				json.WriteString("kind", instance.Node.Kind == TargetKind.Command ? "command" : "target");
				json.WriteBoolean("hasBody", instance.Spec.Body is not null);
				WriteSkipped(json, instance.Skipped);
				json.WriteStartArray("waitsFor");
				foreach (var prerequisite in direct[instance])
					json.WriteStringValue(prerequisite.Name);
				json.WriteEndArray();
				json.WriteStartArray("stages");
				foreach (var stage in instance.Stages)
				{
					json.WriteStartObject();
					json.WriteString("kind", stage.Kind == StageKind.Requires ? "requires" : "composes");
					json.WriteBoolean("parallel", stage.Parallel);
					json.WriteStartArray("items");
					foreach (var item in stage.Items)
					{
						json.WriteStartObject();
						json.WriteString("name", item.Name);
						WriteSkipped(json, item.Skipped);
						json.WriteEndObject();
					}
					json.WriteEndArray();
					json.WriteEndObject();
				}
				json.WriteEndArray();
				json.WriteEndObject();
			}
			json.WriteEndArray();
			json.WriteEndObject();
		}
		output.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
	}

	private static void WriteSkipped(Utf8JsonWriter json, SkipReason? reason)
	{
		switch (reason)
		{
			case SkipReason.SingleTarget:
				json.WriteString("skipped", "single-target");
				break;
			case SkipReason.Condition:
				json.WriteString("skipped", "condition");
				break;
			case SkipReason.Excluded:
				json.WriteString("skipped", "excluded");
				break;
			default:
				json.WriteNull("skipped");
				break;
		}
	}
}
