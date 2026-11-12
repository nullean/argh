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
	private Func<Task>? _asyncBody;
	private readonly TargetKind _kind;
	private readonly List<CaseRef> _deps = new();
	private readonly List<CaseRef> _comps = new();

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
		_asyncBody = body;
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn(params TUnion[] deps)
	{
		foreach (var dep in deps)
			_deps.Add(ToRef(dep));
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1>()
	{
		_deps.Add(new CaseRef(typeof(T1).Name, null));
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1, T2>()
	{
		_deps.Add(new CaseRef(typeof(T1).Name, null));
		_deps.Add(new CaseRef(typeof(T2).Name, null));
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3>()
	{
		_deps.Add(new CaseRef(typeof(T1).Name, null));
		_deps.Add(new CaseRef(typeof(T2).Name, null));
		_deps.Add(new CaseRef(typeof(T3).Name, null));
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3, T4>()
	{
		_deps.Add(new CaseRef(typeof(T1).Name, null));
		_deps.Add(new CaseRef(typeof(T2).Name, null));
		_deps.Add(new CaseRef(typeof(T3).Name, null));
		_deps.Add(new CaseRef(typeof(T4).Name, null));
		return this;
	}

	public IUnionTargetBuilder<TUnion> Composes(params TUnion[] targets)
	{
		foreach (var t in targets)
			_comps.Add(ToRef(t));
		return this;
	}

	public IUnionTargetBuilder<TUnion> Composes<T1>()
	{
		_comps.Add(new CaseRef(typeof(T1).Name, null));
		return this;
	}

	// ── Internal accessors ────────────────────────────────────────────────────

	internal TargetKind Kind => _kind;
	internal string? DescriptionValue => _description;
	internal bool IsHidden => _hidden;
	internal Action? SyncBody => _syncBody;
	internal Func<Task>? AsyncBody => _asyncBody;
	internal IReadOnlyList<CaseRef> Deps => _deps;
	internal IReadOnlyList<CaseRef> Comps => _comps;

	internal async Task ExecuteAsync()
	{
		if (_asyncBody is not null)
			await _asyncBody();
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
