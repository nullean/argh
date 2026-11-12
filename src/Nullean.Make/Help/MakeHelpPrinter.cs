using Nullean.Argh.Help;
using Nullean.Make.Discovery;
using Nullean.Make.Execution;
using Nullean.Make.Parsing;

namespace Nullean.Make.Help;

internal static class MakeHelpPrinter
{
	public static void PrintRoot(BuildGraph graph, string scriptName)
	{
		Console.WriteLine();
		Console.WriteLine(CliHelpFormatting.Section(graph.AppName));
		if (!string.IsNullOrEmpty(graph.AppDescription))
			Console.WriteLine($"  {graph.AppDescription}");
		Console.WriteLine();

		// Separate targets, commands, and namespaces
		var targets = graph.Targets.Where(t => !t.Hidden && t.Kind == TargetKind.Target && t.Route.Length == 1).ToList();
		var commands = graph.Targets.Where(t => !t.Hidden && t.Kind == TargetKind.Command && t.Route.Length == 1).ToList();
		var nsTargets = graph.Targets.Where(t => !t.Hidden && t.Route.Length > 1).ToList();
		var nsNames = nsTargets.Select(t => t.Route[0]).Distinct().OrderBy(x => x).ToList();

		Console.WriteLine($"  {CliHelpFormatting.Section("Usage:")}");
		Console.WriteLine($"    {scriptName} <command|target> [options]");
		if (nsNames.Count > 0)
			Console.WriteLine($"    {scriptName} <namespace> <target> [options]");
		if (graph.Default is { } defaultTarget)
			Console.WriteLine($"    {scriptName}   (runs {string.Join(" ", defaultTarget.Route)} when no target is named)");
		Console.WriteLine();

		if (commands.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Commands:")}  (pipeline entry points — compose and sequence targets)");
			var colWidth = commands.Max(t => t.Name.Length) + 2;
			foreach (var c in commands)
			{
				var desc = !string.IsNullOrEmpty(c.Description) ? c.Description : CommandSummary(c);
				CliHelpFormatting.WriteHelpListNameAndDescription(true, c.Name, desc, colWidth);
			}
			Console.WriteLine();
		}

		if (targets.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Targets:")}  (atomic steps — can also be run directly)");
			var colWidth = targets.Max(t => t.Name.Length) + 2;
			foreach (var t in targets)
			{
				var deps = t.RequiresResolved.Concat(t.ComposesResolved).ToList();
				var depSuffix = deps.Count > 0 ? $"  (depends on: {string.Join(", ", deps.Select(d => string.Join(" ", d.Route)))})" : "";
				CliHelpFormatting.WriteHelpListNameAndDescription(false, t.Name, t.Description + depSuffix, colWidth);
			}
			Console.WriteLine();
		}

		if (nsNames.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Namespaces:")}");
			var colWidth = nsNames.Max(n => n.Length) + 2;
			foreach (var ns in nsNames)
				CliHelpFormatting.WriteHelpListNameAndDescription(true, ns, null, colWidth);
			Console.WriteLine();
		}

		PrintGlobalOptions(graph, scriptName);
	}

	/// <summary>
	/// Prints the commands and targets under a namespace route such as <c>schema</c>.
	/// Returns false when no visible target lives under <paramref name="routeKey"/>.
	/// </summary>
	public static bool PrintNamespace(string routeKey, BuildGraph graph, string scriptName)
	{
		var prefix = routeKey.Split('/');
		var members = graph
			.Targets
			.Where(t => !t.Hidden && t.Route.Length > prefix.Length && t.Route.Take(prefix.Length).SequenceEqual(prefix, StringComparer.OrdinalIgnoreCase))
			.ToList();
		if (members.Count == 0)
			return false;

		var route = string.Join(" ", prefix);
		Console.WriteLine();
		Console.WriteLine($"  {CliHelpFormatting.Accent(route)}");
		Console.WriteLine();

		Console.WriteLine($"  {CliHelpFormatting.Section("Usage:")}");
		Console.WriteLine($"    {scriptName} {route} <command|target> [options]");
		Console.WriteLine();

		string rel(TargetNode t) => string.Join(" ", t.Route.Skip(prefix.Length));

		var commands = members.Where(t => t.Kind == TargetKind.Command).ToList();
		var targets = members.Where(t => t.Kind == TargetKind.Target).ToList();

		if (commands.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Commands:")}");
			var colWidth = commands.Max(t => rel(t).Length) + 2;
			foreach (var c in commands)
			{
				var desc = !string.IsNullOrEmpty(c.Description) ? c.Description : CommandSummary(c);
				CliHelpFormatting.WriteHelpListNameAndDescription(true, rel(c), desc, colWidth);
			}
			Console.WriteLine();
		}

		if (targets.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Targets:")}");
			var colWidth = targets.Max(t => rel(t).Length) + 2;
			foreach (var t in targets)
			{
				var deps = t.RequiresResolved.Concat(t.ComposesResolved).ToList();
				var depSuffix = deps.Count > 0 ? $"  (depends on: {string.Join(", ", deps.Select(d => string.Join(" ", d.Route)))})" : "";
				CliHelpFormatting.WriteHelpListNameAndDescription(false, rel(t), t.Description + depSuffix, colWidth);
			}
			Console.WriteLine();
		}

		PrintGlobalOptions(graph, scriptName);
		return true;
	}

	public static void PrintTarget(TargetNode node, BuildGraph graph, string scriptName)
	{
		Console.WriteLine();
		var route = string.Join(" ", node.Route);
		Console.WriteLine($"  {CliHelpFormatting.Accent(route)} — {node.Description ?? "(no description)"}");
		Console.WriteLine();

		Console.WriteLine($"  {CliHelpFormatting.Section("Usage:")}");
		var optionSuffix = node.DtoType is not null ? " [options]" : "";
		Console.WriteLine($"    {scriptName} {route}{optionSuffix}");
		Console.WriteLine();

		if (node.DtoType is not null)
		{
			PrintDtoOptions(node.DtoType);
			Console.WriteLine();
		}

		var allDeps = node.RequiresResolved.Concat(node.ComposesResolved).ToList();
		if (allDeps.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Depends on:")}");
			foreach (var dep in allDeps)
				Console.WriteLine($"    {string.Join(" ", dep.Route)}");
			Console.WriteLine();
			PrintRunOrder(node, graph, scriptName);
		}

		PrintGlobalOptions(graph, scriptName);
	}

	public static void PrintCommand(TargetNode node, BuildGraph graph, string scriptName)
	{
		Console.WriteLine();
		var route = string.Join(" ", node.Route);
		Console.WriteLine($"  {CliHelpFormatting.Accent(route)} — {node.Description ?? "(no description)"}");
		Console.WriteLine();

		Console.WriteLine($"  {CliHelpFormatting.Section("Usage:")}");
		Console.WriteLine($"    {scriptName} {route} [-s] [global options]");
		Console.WriteLine();

		var num = 1;
		if (node.RequiresResolved.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Requires")} (skippable with -s):");
			foreach (var dep in node.RequiresResolved)
				Console.WriteLine($"    {num++,2}. {CliHelpFormatting.Placeholder(string.Join(" ", dep.Route))}");
			Console.WriteLine();
		}

		if (node.ComposesResolved.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Composes")} (always runs):");
			foreach (var dep in node.ComposesResolved)
				Console.WriteLine($"    {num++,2}. {CliHelpFormatting.Placeholder(string.Join(" ", dep.Route))}");
			Console.WriteLine();
		}

		PrintRunOrder(node, graph, scriptName);
		PrintGlobalOptions(graph, scriptName);
	}

	/// <summary>
	/// <c>Runs:</c> the bodies a bare invocation of the target runs, in order, and where to see the whole plan. Prints
	/// nothing when the target has no dependencies to order, or the plan cannot be built.
	/// </summary>
	private static void PrintRunOrder(TargetNode node, BuildGraph graph, string scriptName)
	{
		if (graph.Source is not { } source || node.RequiresResolved.Count + node.ComposesResolved.Count == 0)
			return;
		RunPlan plan;
		try
		{
			plan = RunPlan.Build([new DepRef(node, null)], source, new PlanOptions(SingleTarget: false));
		}
		catch (MakeException)
		{
			return;
		}
		Console.WriteLine($"  {CliHelpFormatting.Section("Runs:")}");
		Console.WriteLine($"    {PlanRenderer.RunOrder(plan)}");
		Console.WriteLine($"    {CliHelpFormatting.DocRemarksLine($"{scriptName} {string.Join(" ", node.Route)} --graph draws the plan")}");
		Console.WriteLine();
	}

	private static string CommandSummary(TargetNode node)
	{
		var req = node.RequiresResolved.Select(d => string.Join(" ", d.Route)).ToList();
		var comp = node.ComposesResolved.Select(d => string.Join(" ", d.Route)).ToList();
		if (req.Count == 0 && comp.Count == 0)
			return "";
		if (req.Count == 0)
			return string.Join(", ", comp);
		if (comp.Count == 0)
			return string.Join(", ", req);
		return $"{string.Join(", ", req)} → {string.Join(", ", comp)}";
	}

	private static void PrintDtoOptions(Type dtoType)
	{
		Console.WriteLine($"  {CliHelpFormatting.Section("Options:")}");
		foreach (var o in DtoOptions.Describe(dtoType))
		{
			var typeName = DtoOptions.FriendlyTypeName(o.Type);
			var defaultStr = o.HasDefault ? $"  (default: {o.Default ?? "null"})" : "";

			if (o.IsRest)
				Console.WriteLine($"    {CliHelpFormatting.Placeholder($"-- <{o.Name}...>"),-30} {typeName}  (everything after --)");
			else if (o.IsPositional)
				Console.WriteLine($"    {CliHelpFormatting.Placeholder($"<{o.Name}>"),-30} {typeName}{defaultStr}");
			else
				Console.WriteLine($"    {CliHelpFormatting.Placeholder($"--{o.Name} <{typeName}>"),-30}{defaultStr}");
		}
	}

	private static void PrintGlobalOptions(BuildGraph graph, string scriptName)
	{
		Console.WriteLine($"  {CliHelpFormatting.Section("Global options:")}");
		Console.WriteLine($"    {CliHelpFormatting.Placeholder("-s, --single-target"),-30}  Skip prerequisite deps; run only the body / Composes");
		Console.WriteLine($"    {CliHelpFormatting.Placeholder("-h, --help"),-30}  Show this help");
		Console.WriteLine($"    {CliHelpFormatting.Placeholder("    --graph [<format>]"),-30}  Draw what would run and run nothing: tree, mermaid or json");

		foreach (var opt in graph.GlobalOptions)
		{
			var flags = opt.Short is not null ? $"{opt.Short}, {opt.Long}" : $"    {opt.Long}";
			var valueType = opt.Property?.PropertyType ?? opt.ValueType;
			var typeSuffix = opt.IsFlag ? "" : $" <{(valueType is not null ? DtoOptions.FriendlyTypeName(valueType) : "string")}>";
			Console.WriteLine($"    {CliHelpFormatting.Placeholder($"{flags}{typeSuffix}"),-30}  {opt.Description ?? ""}");
		}
		Console.WriteLine();
	}
}
