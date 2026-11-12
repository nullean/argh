using Nullean.Make.Discovery;

namespace Nullean.Make.UnionGraph;

/// <summary>
/// Internal implementation of <see cref="IUnionTargetBuilder{TUnion}"/>.
/// A fresh instance is returned by every call to <c>app.Target()</c> / <c>app.Command()</c>.
/// <see cref="MakeApp{TUnion}"/> calls <c>app.Bind</c> twice: at graph-build time
/// (reads Kind/Description/DepTypeNames) and at execution time (reads SyncBody/AsyncBody).
/// </summary>
/// <summary>A dependency on a union case: its case type name, and the case value, or <c>null</c> for the default one.</summary>
internal readonly record struct CaseRef(string TypeName, object? Value);

internal sealed class UnionTargetBuilderImpl<TUnion> : IUnionTargetBuilder<TUnion>
{
	private string? _description;
	private bool _hidden;
	private Action? _syncBody;
	private Func<CancellationToken, Task>? _asyncBody;
	private readonly TargetKind _kind;
	private readonly List<(Execution.StageKind Kind, bool Parallel, List<CaseRef> Items)> _stages = new();
	private readonly List<CaseRef> _after = new();
	private readonly List<CaseRef> _cleanup = new();
	private readonly List<string> _needsEnv = new();
	private readonly List<string> _needsTool = new();
	private Func<bool>? _onlyWhen;

	internal UnionTargetBuilderImpl(TargetKind kind = TargetKind.Target) => _kind = kind;

	// ── IUnionTargetBuilder<TUnion> ──────────────────────────────────────────

	public IUnionTargetBuilder<TUnion> Description(string text)
	{
		_description = text;
		return this;
	}
	public IUnionTargetBuilder<TUnion> Hidden()
	{
		_hidden = true;
		return this;
	}
	public IUnionTargetBuilder<TUnion> Executes(Action body)
	{
		_syncBody = body;
		return this;
	}
	public IUnionTargetBuilder<TUnion> Executes(Func<Task> body)
	{
		_asyncBody = _ => body();
		return this;
	}

	public IUnionTargetBuilder<TUnion> Executes(Func<CancellationToken, Task> body)
	{
		_asyncBody = body;
		return this;
	}

	public IUnionTargetBuilder<TUnion> Requires(params TUnion[] gates) => DependsOn(gates);

	public IUnionTargetBuilder<TUnion> OnlyWhen(Func<bool> condition)
	{
		_onlyWhen = condition;
		return this;
	}

	public IUnionTargetBuilder<TUnion> After(params TUnion[] targets)
	{
		_after.AddRange(targets.Select(ToRef));
		return this;
	}

	public IUnionTargetBuilder<TUnion> Cleanup(params TUnion[] targets)
	{
		_cleanup.AddRange(targets.Select(ToRef));
		return this;
	}

	public IUnionTargetBuilder<TUnion> NeedsEnv(params string[] variables)
	{
		_needsEnv.AddRange(variables);
		return this;
	}

	public IUnionTargetBuilder<TUnion> NeedsTool(params string[] tools)
	{
		_needsTool.AddRange(tools);
		return this;
	}

	public IUnionTargetBuilder<TUnion> Parallel(params TUnion[] targets)
	{
		_stages.Add((Execution.StageKind.Composes, true, targets.Select(ToRef).ToList()));
		return this;
	}

	/// <summary>Adds to the last stage when it is of the same kind and sequential, else starts a stage.</summary>
	private void Append(Execution.StageKind kind, CaseRef reference)
	{
		if (_stages.Count > 0 && _stages[^1].Kind == kind && !_stages[^1].Parallel)
			_stages[^1].Items.Add(reference);
		else
			_stages.Add((kind, false, [reference]));
	}

	public IUnionTargetBuilder<TUnion> DependsOn(params TUnion[] deps)
	{
		foreach (var dep in deps)
			Append(Execution.StageKind.Requires, ToRef(dep));
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1>()
	{
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T1).Name, null));
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1, T2>()
	{
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T1).Name, null));
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T2).Name, null));
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3>()
	{
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T1).Name, null));
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T2).Name, null));
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T3).Name, null));
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3, T4>()
	{
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T1).Name, null));
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T2).Name, null));
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T3).Name, null));
		Append(Execution.StageKind.Requires, new CaseRef(typeof(T4).Name, null));
		return this;
	}

	public IUnionTargetBuilder<TUnion> Composes(params TUnion[] targets)
	{
		foreach (var t in targets)
			Append(Execution.StageKind.Composes, ToRef(t));
		return this;
	}

	public IUnionTargetBuilder<TUnion> Composes<T1>()
	{
		Append(Execution.StageKind.Composes, new CaseRef(typeof(T1).Name, null));
		return this;
	}

	// ── Internal accessors ────────────────────────────────────────────────────

	internal TargetKind Kind => _kind;
	internal string? DescriptionValue => _description;
	internal bool IsHidden => _hidden;
	internal Action? SyncBody => _syncBody;
	internal Func<CancellationToken, Task>? AsyncBody => _asyncBody;
	internal IReadOnlyList<CaseRef> Deps => _stages.Where(s => s.Kind == Execution.StageKind.Requires).SelectMany(s => s.Items).ToList();
	internal IReadOnlyList<CaseRef> Comps => _stages.Where(s => s.Kind == Execution.StageKind.Composes).SelectMany(s => s.Items).ToList();
	internal IReadOnlyList<(Execution.StageKind Kind, bool Parallel, List<CaseRef> Items)> Stages => _stages;
	internal IReadOnlyList<CaseRef> AfterRefs => _after;
	internal IReadOnlyList<CaseRef> CleanupRefs => _cleanup;
	internal IReadOnlyList<string> Env => _needsEnv;
	internal IReadOnlyList<string> Tools => _needsTool;
	internal Func<bool>? Condition => _onlyWhen;

	internal async Task ExecuteAsync(CancellationToken token)
	{
		if (_asyncBody is not null)
			await _asyncBody(token);
		else
			_syncBody?.Invoke();
	}

	/// <summary>
	/// The innermost case of a union value, flat or nested, from the generated case table: its type name, and the case
	/// value the dependency runs with.
	/// </summary>
	private static CaseRef ToRef(TUnion dep)
	{
		var leafType = UnionCaseTable<TUnion>.LeafType ?? throw new MakeException($"No case table was generated for {typeof(TUnion).Name}.", 1);
		return new CaseRef(leafType(dep)?.Name ?? "", UnionCaseTable<TUnion>.LeafValue?.Invoke(dep));
	}
}
