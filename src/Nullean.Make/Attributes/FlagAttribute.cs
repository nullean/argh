namespace Nullean.Make;

/// <summary>
/// Marks a <see cref="bool"/> property on a <see cref="MakeBuild"/> subclass as a global boolean flag.
/// Presence of the flag on the command line sets the property to <see langword="true"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FlagAttribute : Attribute
{
	/// <summary>Long flag name, e.g. <c>"--clean-checkout"</c>.</summary>
	public string Long { get; }

	/// <summary>Short flag name, e.g. <c>"-c"</c>. Optional.</summary>
	public string? Short { get; set; }

	/// <summary>One-line description shown in help output.</summary>
	public string? Description { get; set; }

	public FlagAttribute(string @long, string? @short = null)
	{
		Long = @long;
		Short = @short;
	}
}
