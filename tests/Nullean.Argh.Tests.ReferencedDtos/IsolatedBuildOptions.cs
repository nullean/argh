namespace Nullean.Argh.Tests.ReferencedDtos;

public sealed record IsolatedBuildOptions
{
	/// <summary>-p, Documentation root. Defaults to cwd/docs.</summary>
	public string? Path { get; init; }

	/// <summary>Output directory. Defaults to .artifacts/html.</summary>
	public string? Output { get; init; }
}
