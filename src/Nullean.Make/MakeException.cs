namespace Nullean.Make;

/// <summary>Thrown from a target body to fail the make run with a message and optional exit code.</summary>
public sealed class MakeException : Exception
{
	public int ExitCode { get; }

	public MakeException(string message, int exitCode = 1) : base(message) => ExitCode = exitCode;
}
