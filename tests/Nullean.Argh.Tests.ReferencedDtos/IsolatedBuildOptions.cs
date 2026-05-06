namespace Nullean.Argh.Tests.ReferencedDtos;

/// <summary>Source kind for cross-assembly nullable-enum regression coverage.</summary>
public enum IsolatedSource { File, Remote }

public sealed record IsolatedBuildOptions
{
	/// <summary>-p, Documentation root. Defaults to cwd/docs.</summary>
	public string? Path { get; init; }

	/// <summary>Output directory. Defaults to .artifacts/html.</summary>
	public string? Output { get; init; }

	/// <summary>Source kind (cross-assembly nullable enum — Nullable&lt;T&gt; must not generate CS8600).</summary>
	public IsolatedSource? Source { get; init; }
}
