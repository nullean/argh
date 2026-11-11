using Nullean.Make.Discovery;

namespace Nullean.Make.UnionGraph;

/// <summary>
/// Internal implementation of <see cref="IUnionTargetBuilder{TUnion}"/>.
/// A fresh instance is returned by every call to <c>app.Target()</c> / <c>app.Command()</c>.
/// <see cref="MakeApp{TUnion}"/> calls <c>app.Bind</c> twice: at graph-build time
/// (reads Kind/Description/DepTypeNames) and at execution time (reads SyncBody/AsyncBody).
/// </summary>
internal sealed class UnionTargetBuilderImpl<TUnion> : IUnionTargetBuilder<TUnion>
{
	private string? _description;
	private bool _hidden;
	private Action? _syncBody;
	private Func<Task>? _asyncBody;
	private readonly TargetKind _kind;
	private readonly List<string> _depTypeNames = new();
	private readonly List<string> _compTypeNames = new();

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
			_depTypeNames.Add(GetLeafCaseName(dep));
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1>()
	{
		_depTypeNames.Add(typeof(T1).Name);
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1, T2>()
	{
		_depTypeNames.Add(typeof(T1).Name);
		_depTypeNames.Add(typeof(T2).Name);
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3>()
	{
		_depTypeNames.Add(typeof(T1).Name);
		_depTypeNames.Add(typeof(T2).Name);
		_depTypeNames.Add(typeof(T3).Name);
		return this;
	}

	public IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3, T4>()
	{
		_depTypeNames.Add(typeof(T1).Name);
		_depTypeNames.Add(typeof(T2).Name);
		_depTypeNames.Add(typeof(T3).Name);
		_depTypeNames.Add(typeof(T4).Name);
		return this;
	}

	public IUnionTargetBuilder<TUnion> Composes(params TUnion[] targets)
	{
		foreach (var t in targets)
			_compTypeNames.Add(GetLeafCaseName(t));
		return this;
	}

	public IUnionTargetBuilder<TUnion> Composes<T1>()
	{
		_compTypeNames.Add(typeof(T1).Name);
		return this;
	}

	// ── Internal accessors ────────────────────────────────────────────────────

	internal TargetKind Kind => _kind;
	internal string? DescriptionValue => _description;
	internal bool IsHidden => _hidden;
	internal Action? SyncBody => _syncBody;
	internal Func<Task>? AsyncBody => _asyncBody;
	internal IReadOnlyList<string> DepTypeNames => _depTypeNames;
	internal IReadOnlyList<string> CompTypeNames => _compTypeNames;

	internal async Task ExecuteAsync()
	{
		if (_asyncBody is not null)
			await _asyncBody();
		else
			_syncBody?.Invoke();
	}

	/// <summary>The innermost case type name of a union value, flat or nested, from the generated case table.</summary>
	private static string GetLeafCaseName(TUnion dep) =>
		(UnionCaseTable<TUnion>.LeafType ?? throw new MakeException($"No case table was generated for {typeof(TUnion).Name}.", 1))(dep)?.Name ?? "";
}
