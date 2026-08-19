namespace Nullean.Make.UnionGraph;

/// <summary>
/// Mutable handle to a global option declared on <see cref="UnionMakeApp{TUnion}"/>.
/// Obtain via <see cref="UnionMakeApp{TUnion}.Flag"/> or <see cref="UnionMakeApp{TUnion}.Option{T}"/>.
/// The value is pre-populated during argv extraction, before any target body runs.
/// </summary>
public sealed class UnionOptionRef<T>
{
	private readonly Func<string, T> _parser;
	private T _value;

	internal UnionOptionRef(string longName, string? shortName, string? description, T defaultValue, Func<string, T> parser)
	{
		Long        = longName;
		Short       = shortName;
		Description = description;
		DefaultValue = defaultValue;
		_value      = defaultValue;
		_parser     = parser;
	}

	/// <summary>Long flag name, e.g. <c>--token</c>.</summary>
	public string Long { get; }

	/// <summary>Short flag name, e.g. <c>-t</c>. Null when not declared.</summary>
	public string? Short { get; }

	/// <summary>Description shown in help output.</summary>
	public string? Description { get; }

	/// <summary>Default value used when the flag is not supplied.</summary>
	public T DefaultValue { get; }

	/// <summary>Current parsed value — valid after <c>RunAsync</c> begins argv extraction.</summary>
	public T Value => _value;

	internal void Set(string raw) => _value = _parser(raw);
	internal void Reset()         => _value = DefaultValue;
}

internal sealed record UnionOptionDecl(
	string Long,
	string? Short,
	string? Description,
	bool IsFlag,
	Action<string> Set);
