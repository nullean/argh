using Nullean.Make.Discovery;

namespace Nullean.Make.Execution;

/// <summary>
/// What a surface (the <see cref="MakeApp{TUnion}"/> union, the <see cref="MakeBuild"/> class, the F# DU) tells the
/// planner about its targets. A target runs as an instance: a <see cref="TargetNode"/> and a value, the case record
/// or DTO it was named with. Two references to one node with equal values are one instance; with different values,
/// two.
/// </summary>
internal interface ITargetSource
{
	/// <summary>The value a reference without one stands for. <c>null</c> for a target that takes no arguments.</summary>
	object? DefaultValue(TargetNode node);

	/// <summary>Binds the command-line tokens that followed the route into the node's value.</summary>
	object? BindArguments(TargetNode node, string[] args);

	/// <summary>The instance's dependencies and body, for this value.</summary>
	InstanceSpec Describe(TargetNode node, object? value);

	/// <summary>
	/// How the value reads after the route, e.g. <c>filter: Unit</c>: the fields that differ from the default value.
	/// <c>null</c> or empty when the value is the default.
	/// </summary>
	string? FormatValue(TargetNode node, object? value);
}

/// <summary>Whether a stage is a gate that <c>-s</c> skips on the named target, or the work it always runs.</summary>
internal enum StageKind
{
	/// <summary>A target's dependencies, or a command's requires. Skipped on the named target under <c>-s</c>.</summary>
	Requires,

	/// <summary>A command's composes. Always runs.</summary>
	Composes
}

/// <summary>A reference from one instance to another. A <c>null</c> value stands for the node's default value.</summary>
internal readonly record struct DepRef(TargetNode Node, object? Value);

/// <summary>
/// One step in an instance's dependencies. A sequential stage runs its items in order; a parallel stage's items do
/// not wait for each other. Every item waits for the stage before it.
/// </summary>
internal sealed record Stage(StageKind Kind, bool Parallel, IReadOnlyList<DepRef> Items);

/// <summary>What an instance needs before it runs, and what it runs.</summary>
internal sealed class InstanceSpec
{
	public List<Stage> Stages { get; } = new();

	/// <summary>The body, or <c>null</c> for a command that only sequences other targets.</summary>
	public Func<CancellationToken, Task>? Body { get; set; }

	/// <summary>When it returns <see langword="false"/>, the body is skipped. Its dependencies still run.</summary>
	public Func<bool>? OnlyWhen { get; set; }

	/// <summary>Targets this one runs after when they are in the same run. They do not join the run because of it.</summary>
	public List<DepRef> After { get; } = new();

	/// <summary>Targets whose bodies run at the end of the run when this one started, even after a failure.</summary>
	public List<DepRef> Cleanup { get; } = new();

	/// <summary>Environment variables that must be set before the run starts.</summary>
	public List<string> NeedsEnv { get; } = new();

	/// <summary>Executables that must be on the <c>PATH</c> before the run starts.</summary>
	public List<string> NeedsTool { get; } = new();

	public void Add(StageKind kind, IReadOnlyList<DepRef> items, bool parallel = false)
	{
		if (items.Count > 0)
			Stages.Add(new Stage(kind, parallel, items));
	}
}
