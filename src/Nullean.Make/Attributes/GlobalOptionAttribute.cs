namespace Nullean.Make;

/// <summary>
/// Marks a property on a <see cref="MakeBuild"/> subclass as a global CLI option available to all targets.
/// The property value is populated from the command line before any target runs.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GlobalOptionAttribute : Attribute
{
	/// <summary>Long flag name, e.g. <c>"--token"</c>.</summary>
	public string Long { get; }

	/// <summary>Short flag name, e.g. <c>"-t"</c>. Optional.</summary>
	public string? Short { get; set; }

	/// <summary>One-line description shown in help output.</summary>
	public string? Description { get; set; }

	public GlobalOptionAttribute(string @long) => Long = @long;
}
