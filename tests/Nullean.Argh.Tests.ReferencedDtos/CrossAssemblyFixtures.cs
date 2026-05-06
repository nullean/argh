namespace Nullean.Argh.Tests.ReferencedDtos;

/// <summary>Severity enum defined in a separate assembly to exercise cross-assembly options parsing.</summary>
public enum CrossAssemblyLevel { Trace, Information, Warning, Error }

/// <summary>
/// DTO defined in a separate assembly.
/// Tests that non-nullable properties with C# initializers are NOT required when the type
/// comes from a referenced project (DeclaringSyntaxReferences is empty in the consuming compilation).
/// </summary>
public record CrossAssemblyAsParamsDto
{
	/// <summary>Level — non-nullable with a non-zero default.</summary>
	public CrossAssemblyLevel Level { get; init; } = CrossAssemblyLevel.Information;

	/// <summary>Arbitrary tag string with an explicit non-empty default.</summary>
	public string Tag { get; init; } = "default-tag";
}
