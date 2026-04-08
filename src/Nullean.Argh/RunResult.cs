namespace Nullean.Argh;

/// <summary>
/// Outcome of a captured CLI run: process exit code and redirected standard streams.
/// </summary>
public sealed class RunResult
{
	/// <summary>The integer exit code returned by the runner.</summary>
	public int ExitCode { get; }

	/// <summary>Text written to standard output while capture was active.</summary>
	public string Stdout { get; }

	/// <summary>Text written to standard error while capture was active.</summary>
	public string Stderr { get; }

	/// <summary>Creates a result with the given exit code and captured streams.</summary>
	public RunResult(int exitCode, string stdout, string stderr)
	{
		ExitCode = exitCode;
		Stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
		Stderr = stderr ?? throw new ArgumentNullException(nameof(stderr));
	}
}
