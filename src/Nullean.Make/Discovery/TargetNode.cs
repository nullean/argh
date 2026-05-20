using System.Reflection;

namespace Nullean.Make.Discovery;

internal enum TargetKind { Target, Command }

internal sealed class TargetNode
{
	/// <summary>CLI path segments, e.g. ["pkg", "generate"] or ["clean"].</summary>
	public required string[] Route { get; init; }

	/// <summary>CLI name of the leaf segment (last element of <see cref="Route"/>).</summary>
	public string Name => Route[^1];

	public string? Description { get; set; }
	public bool Hidden { get; set; }
	public Func<bool>? OnlyWhen { get; set; }
	public TargetKind Kind { get; set; }

	/// <summary>DependsOn / Requires methods — skippable under -s.</summary>
	public List<MethodInfo> Requires { get; } = new();

	/// <summary>Composes methods — always run.</summary>
	public List<MethodInfo> Composes { get; } = new();

	/// <summary>Sync body; null when async or not set.</summary>
	public Action? SyncBody { get; set; }

	/// <summary>Async body; null when sync or not set.</summary>
	public Func<Task>? AsyncBody { get; set; }

	/// <summary>The type of the per-target DTO (null for plain Target/Command).</summary>
	public Type? DtoType { get; set; }

	/// <summary>Typed body delegate: Action&lt;T&gt; or Func&lt;T,Task&gt;. Null when no DTO.</summary>
	public Delegate? TypedBody { get; set; }

	/// <summary>Resolved dep nodes after the graph is built.</summary>
	public List<TargetNode> RequiresResolved { get; } = new();
	public List<TargetNode> ComposesResolved { get; } = new();

	/// <summary>MethodInfo of the configure delegate — stable identity across property getter invocations. Null for F# DU-based nodes.</summary>
	public MethodInfo? ConfigureMethod { get; init; }
}
