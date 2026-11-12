using Nullean.Make.Discovery;

namespace Nullean.Make.Execution;

/// <summary>Why a target in the plan does not run.</summary>
internal enum SkipReason
{
	/// <summary><c>-s</c> skipped a gate of the named target.</summary>
	SingleTarget,

	/// <summary>Its condition returned <see langword="false"/>, so its body does not run.</summary>
	Condition
}

/// <summary>One target with one value in a run: it runs once, however many targets depend on it.</summary>
internal sealed class TargetInstance
{
	public required TargetNode Node { get; init; }
	public required object? Value { get; init; }
	public required InstanceSpec Spec { get; init; }

	/// <summary>The route, and the value when it is not the default: <c>test (filter: Unit)</c>.</summary>
	public required string Name { get; init; }

	/// <summary>Position in the plan. A prerequisite always comes earlier.</summary>
	public int Order { get; set; }

	/// <summary>True for a target named on the command line.</summary>
	public bool IsRoot { get; set; }

	/// <summary>Everything that has to finish before this instance starts: its stages, and what it waits for where it was first reached.</summary>
	public List<TargetInstance> Prerequisites { get; } = new();

	/// <summary>The stages as the plan resolved them, for drawing the plan.</summary>
	public List<PlannedStage> Stages { get; } = new();

	/// <summary>Set when the planner evaluated the condition and it returned <see langword="false"/>.</summary>
	public SkipReason? Skipped { get; set; }
}

/// <summary>A stage of an instance with its items resolved.</summary>
internal sealed record PlannedStage(StageKind Kind, bool Parallel, IReadOnlyList<PlannedItem> Items);

/// <summary>An item of a stage: the instance it runs, or the name of one the plan skips.</summary>
internal sealed record PlannedItem(TargetInstance? Instance, string Name, SkipReason? Skipped);

/// <summary>Options that change which instances the plan contains.</summary>
/// <param name="SingleTarget">Skip the named targets' gates, as <c>-s</c> does.</param>
/// <param name="EvaluateConditions">Evaluate each condition while planning, so a drawn plan shows what it skips.</param>
/// <param name="ChainRoots">Run the named targets in order, each after the one before it.</param>
internal sealed record PlanOptions(bool SingleTarget, bool EvaluateConditions = false, bool ChainRoots = true);

/// <summary>The instances a run contains, in an order that respects every prerequisite.</summary>
internal sealed class RunPlan
{
	public required IReadOnlyList<TargetInstance> Roots { get; init; }
	public required IReadOnlyList<TargetInstance> Instances { get; init; }

	/// <summary>True when the roots run one after another; false for a drawing of unrelated roots.</summary>
	public bool Chained { get; init; } = true;

	/// <summary>For each instance with cleanup targets, the instances that run at the end of the run once it started.</summary>
	public IReadOnlyDictionary<TargetInstance, IReadOnlyList<TargetInstance>> Cleanups
	{
		get;
		init;
	} = new Dictionary<TargetInstance, IReadOnlyList<TargetInstance>>();

	/// <summary>
	/// What the run needs and does not have: an environment variable that is not set, or an executable not on the
	/// <c>PATH</c>, for each instance whose body would run. A target whose condition is false needs nothing.
	/// </summary>
	public IReadOnlyList<(TargetInstance Instance, string Missing)> MissingPreconditions()
	{
		var missing = new List<(TargetInstance, string)>();
		foreach (var instance in Instances.Concat(Cleanups.Values.SelectMany(c => c)).Distinct())
		{
			if (instance.Skipped is not null || instance.Spec.Body is null)
				continue;
			if (instance.Spec.NeedsEnv.Count + instance.Spec.NeedsTool.Count == 0)
				continue;
			if (instance.Spec.OnlyWhen is { } condition && !condition())
				continue;
			foreach (var variable in instance.Spec.NeedsEnv.Where(v => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))))
				missing.Add((instance, $"the environment variable {variable}"));
			foreach (var tool in instance.Spec.NeedsTool.Where(t => !Preconditions.OnPath(t)))
				missing.Add((instance, $"{tool} on the PATH"));
		}
		return missing;
	}

	/// <summary>
	/// Expands the named targets into instances. Each reference resolves to its node and value; a reference without a
	/// value stands for the node's default value. A sequential stage's item waits for the item before it, and every
	/// item waits for the stage before it, where the item is first reached.
	/// </summary>
	public static RunPlan Build(IReadOnlyList<DepRef> roots, ITargetSource source, PlanOptions options)
	{
		var builder = new Builder(source, options);
		var rootInstances = new List<TargetInstance>();
		TargetInstance? previous = null;
		foreach (var root in roots)
		{
			var instance = builder.Visit(root, isRoot: true, previous is null || !options.ChainRoots ? [] : [previous]);
			instance.IsRoot = true;
			rootInstances.Add(instance);
			previous = instance;
		}
		var ordered = builder.ApplyAfter();
		return new RunPlan { Roots = rootInstances, Instances = ordered, Chained = options.ChainRoots, Cleanups = builder.ResolveCleanups(ordered) };
	}

	private sealed class Builder(ITargetSource source, PlanOptions options)
	{
		private readonly Dictionary<(TargetNode Node, object? Value), TargetInstance> _instances = new();
		private readonly Dictionary<TargetNode, object?> _defaults = new();
		private readonly List<TargetInstance> _stack = new();

		public List<TargetInstance> Ordered { get; } = new();

		/// <summary>
		/// Adds what each instance runs after, among the instances in the run, and orders the plan again: the earliest
		/// instance whose prerequisites have all been placed comes next.
		/// </summary>
		public List<TargetInstance> ApplyAfter()
		{
			var any = false;
			foreach (var instance in Ordered)
			{
				foreach (var reference in instance.Spec.After)
				{
					// A reference without a value matches the target with any value.
					var matches = Ordered.Where(
						i => i.Node == reference.Node && (reference.Value is null || Equals(i.Value, reference.Value)) && i != instance
					);
					foreach (var match in matches)
					{
						instance.Prerequisites.Add(match);
						any = true;
					}
				}
			}
			if (!any)
				return Ordered;

			var placed = new HashSet<TargetInstance>();
			var result = new List<TargetInstance>();
			var remaining = Ordered.ToList();
			while (remaining.Count > 0)
			{
				var next = remaining.FirstOrDefault(i => i.Prerequisites.All(placed.Contains));
				if (next is null)
				{
					var stuck = remaining.Select(i => i.Name);
					throw new MakeException($"The order the targets ask for has a cycle; none of these can go first: {string.Join(", ", stuck)}", 1);
				}
				remaining.Remove(next);
				placed.Add(next);
				next.Order = result.Count;
				result.Add(next);
			}
			return result;
		}

		/// <summary>The cleanup instances of each instance that declares some. A cleanup runs its body only, not its dependencies.</summary>
		public Dictionary<TargetInstance, IReadOnlyList<TargetInstance>> ResolveCleanups(List<TargetInstance> ordered)
		{
			var cleanups = new Dictionary<TargetInstance, IReadOnlyList<TargetInstance>>();
			var shared = new Dictionary<(TargetNode, object?), TargetInstance>();
			foreach (var instance in ordered.Where(i => i.Spec.Cleanup.Count > 0))
			{
				var list = new List<TargetInstance>();
				foreach (var reference in instance.Spec.Cleanup)
				{
					var value = Resolve(reference);
					if (!shared.TryGetValue((reference.Node, value), out var cleanup))
					{
						cleanup = new TargetInstance
						{
							Node = reference.Node,
							Value = value,
							Spec = source.Describe(reference.Node, value),
							Name = ValueDisplay.Name(reference.Node, source.FormatValue(reference.Node, value))
						};
						shared[(reference.Node, value)] = cleanup;
					}
					list.Add(cleanup);
				}
				cleanups[instance] = list;
			}
			return cleanups;
		}

		private object? Resolve(DepRef reference)
		{
			if (reference.Value is not null)
				return reference.Value;
			if (!_defaults.TryGetValue(reference.Node, out var value))
				_defaults[reference.Node] = value = source.DefaultValue(reference.Node);
			return value;
		}

		public TargetInstance Visit(DepRef reference, bool isRoot, IReadOnlyList<TargetInstance> waitFor)
		{
			var value = Resolve(reference);
			var key = (reference.Node, value);

			if (_instances.TryGetValue(key, out var existing))
			{
				if (_stack.Contains(existing))
				{
					var path = _stack.SkipWhile(i => i != existing).Select(i => i.Name).Append(existing.Name);
					throw new MakeException($"Dependency cycle detected: {string.Join(" → ", path)}", 1);
				}
				return existing;
			}

			var spec = source.Describe(reference.Node, value);
			var instance = new TargetInstance
			{
				Node = reference.Node,
				Value = value,
				Spec = spec,
				Name = ValueDisplay.Name(reference.Node, source.FormatValue(reference.Node, value))
			};
			_instances[key] = instance;
			instance.Prerequisites.AddRange(waitFor);

			_stack.Add(instance);
			// An instance's own dependencies wait for what it waits for: in `composes [a; b]`, b's dependencies start
			// after a, not alongside it.
			IReadOnlyList<TargetInstance> barrier = waitFor;
			foreach (var stage in spec.Stages)
			{
				var items = new List<PlannedItem>();
				if (isRoot && options.SingleTarget && stage.Kind == StageKind.Requires)
				{
					foreach (var item in stage.Items)
						items.Add(new PlannedItem(null, ValueDisplay.Name(item.Node, source.FormatValue(item.Node, Resolve(item))), SkipReason.SingleTarget));
					instance.Stages.Add(new PlannedStage(stage.Kind, stage.Parallel, items));
					continue;
				}

				var reached = new List<TargetInstance>();
				TargetInstance? previous = null;
				foreach (var item in stage.Items)
				{
					IReadOnlyList<TargetInstance> wait = stage.Parallel || previous is null ? barrier : [.. barrier, previous];
					var child = Visit(item, isRoot: false, wait);
					items.Add(new PlannedItem(child, child.Name, null));
					reached.Add(child);
					previous = child;
				}
				instance.Stages.Add(new PlannedStage(stage.Kind, stage.Parallel, items));
				instance.Prerequisites.AddRange(reached);
				barrier = reached;
			}
			_stack.RemoveAt(_stack.Count - 1);

			if (options.EvaluateConditions && spec.OnlyWhen is { } condition && !condition())
				instance.Skipped = SkipReason.Condition;

			instance.Order = Ordered.Count;
			Ordered.Add(instance);
			return instance;
		}
	}
}

/// <summary>Checks the things a target says it needs before the run starts.</summary>
internal static class Preconditions
{
	/// <summary>Whether <paramref name="tool"/> is an executable on the <c>PATH</c>, with <c>PATHEXT</c> on Windows.</summary>
	public static bool OnPath(string tool)
	{
		if (Path.IsPathRooted(tool))
			return File.Exists(tool);
		var extensions = OperatingSystem.IsWindows()
			? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries).Prepend("")
			: [""];
		var path = Environment.GetEnvironmentVariable("PATH") ?? "";
		foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			foreach (var extension in extensions)
			{
				if (File.Exists(Path.Combine(directory, tool + extension)))
					return true;
			}
		}
		return false;
	}
}
