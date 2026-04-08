namespace Nullean.Argh;

/// <summary>Marks a parameter as a positional CLI argument (successive positions starting at 0).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ArgumentAttribute : Attribute;

/// <summary>Specifies an <see cref="IArgumentParser{T}"/> implementation used to parse this parameter.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ArgumentParserAttribute : Attribute
{
	public ArgumentParserAttribute(Type parserType) => ParserType = parserType;

	public Type ParserType { get; }
}

/// <summary>
/// When applied to a command parameter, binds public primary-constructor parameters and init-only properties
/// of the parameter type as CLI flags (or <see cref="ArgumentAttribute"/> positionals) instead of a single value.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class AsParametersAttribute : Attribute
{
	/// <summary>Optional kebab-case prefix applied to every generated long name (e.g. <c>app</c> → <c>--app-name</c>).</summary>
	public string? Prefix { get; }

	public AsParametersAttribute() => Prefix = null;

	public AsParametersAttribute(string prefix) => Prefix = prefix;
}

/// <summary>
/// Overrides parsing for collection-typed parameters. By default, values are collected from repeated flags
/// (<c>--tag a --tag b</c>). When <see cref="Separator"/> is set, a single flag value is split instead.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CollectionSyntaxAttribute : Attribute
{
	/// <summary>When non-null and non-empty, the flag value is split on this separator; otherwise repeated flags are used.</summary>
	public string? Separator { get; set; }
}

/// <summary>
/// Associates a per-command filter with a command method. The source generator reads this metadata; it has no runtime behavior until the generator emits the filter pipeline.
/// </summary>
/// <typeparam name="TFilter">A type implementing <see cref="ICommandFilter"/>.</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class FilterAttribute<TFilter> : Attribute where TFilter : ICommandFilter;
