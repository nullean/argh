using System.Reflection;

namespace Nullean.Make.Discovery;

internal enum TargetKind
{
	Target,
	Command
}

internal sealed class TargetNode
{
	/// <summary>CLI path segments, e.g. ["pkg", "generate"] or ["clean"].</summary>
	public required string[] Route { get; init; }

	/// <summary>CLI name of the leaf segment (last element of <see cref="Route"/>).</summary>
	public string Name => Route[^1];

	public string? Description { get; set; }
	public bool Hidden { get; set; }
	public Func<bool>? OnlyWhen { get; set; }

	/// <summary>Targets this one runs after when they are in the same run.</summary>
	public List<TargetRef> After { get; } = new();

	/// <summary>Targets whose bodies run at the end of the run once this one started.</summary>
	public List<TargetRef> Cleanup { get; } = new();

	/// <summary>Environment variables that must be set before the run starts.</summary>
	public List<string> NeedsEnv { get; } = new();

	/// <summary>Executables that must be on the <c>PATH</c> before the run starts.</summary>
	public List<string> NeedsTool { get; } = new();
	public TargetKind Kind { get; set; }

	/// <summary>DependsOn / Requires references — skippable under -s.</summary>
	public List<TargetRef> Requires { get; } = new();

	/// <summary>Composes references — always run.</summary>
	public List<TargetRef> Composes { get; } = new();

	/// <summary>The same references as <see cref="Requires"/> and <see cref="Composes"/>, in declared stages.</summary>
	public List<(Execution.StageKind Kind, bool Parallel, List<TargetRef> Items)> Stages { get; } = new();

	/// <summary>Records a reference in its list and in the stages: the last stage when it is of the same kind and sequential.</summary>
	public void AddReference(Execution.StageKind kind, TargetRef reference, bool parallelStage = false)
	{
		(kind == Execution.StageKind.Requires ? Requires : Composes).Add(reference);
		if (!parallelStage && Stages.Count > 0 && Stages[^1].Kind == kind && !Stages[^1].Parallel)
			Stages[^1].Items.Add(reference);
		else
			Stages.Add((kind, parallelStage, [reference]));
	}

	/// <summary>Starts a parallel stage of composed references.</summary>
	public void AddParallel(TargetRef[] references)
	{
		Composes.AddRange(references);
		Stages.Add((Execution.StageKind.Composes, true, [.. references]));
	}

	/// <summary>Sync body; null when async or not set.</summary>
	public Action? SyncBody { get; set; }

	/// <summary>Async body; null when sync or not set.</summary>
	public Func<Task>? AsyncBody { get; set; }

	/// <summary>The type of the per-target DTO (null for plain Target/Command).</summary>
	public Type? DtoType { get; set; }

	/// <summary>Typed body delegate: Action&lt;T&gt; or Func&lt;T,Task&gt;. Null when no DTO.</summary>
	public Delegate? TypedBody { get; set; }

	/// <summary>Route-based dep type names used by <see cref="MakeApp{TUnion}"/> before node resolution.</summary>
	public List<string> RouteRequires { get; } = new();
	public List<string> RouteComposes { get; } = new();

	/// <summary>Resolved dep nodes after the graph is built.</summary>
	public List<TargetNode> RequiresResolved { get; } = new();
	public List<TargetNode> ComposesResolved { get; } = new();

	/// <summary>MethodInfo of the configure delegate — stable identity across property getter invocations. Null for F# DU-based nodes.</summary>
	public MethodInfo? ConfigureMethod { get; init; }
}
