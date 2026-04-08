namespace Nullean.Argh;

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
