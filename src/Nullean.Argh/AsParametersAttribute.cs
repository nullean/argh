namespace Nullean.Argh;

/// <summary>
/// When applied to a command parameter, binds public primary-constructor parameters and init-only properties
/// of the parameter type as CLI flags (or <see cref="ArgumentAttribute"/> positionals) instead of a single value.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class AsParametersAttribute : Attribute
{
	/// <summary>Optional kebab-case prefix applied to every generated long name (e.g. <c>app</c> → <c>--app-name</c>).</summary>
	public string? Prefix { get; }

	public AsParametersAttribute()
	{
		Prefix = null;
	}

	public AsParametersAttribute(string prefix)
	{
		Prefix = prefix;
	}
}
