using System.Text;

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

/// <summary>
/// Helpers for running CLI entry points in tests: captures <see cref="Console.Out"/> and <see cref="Console.Error"/> while the runner executes.
/// </summary>
/// <remarks>
/// Console is process-global; do not run captured runs concurrently on the same process.
/// For a string command line, arguments are split on whitespace; double quotes (<c>"</c>) group segments (minimal shell-like splitting).
/// </remarks>
public static class ArghCli
{
	/// <summary>
	/// Runs <paramref name="runAsync"/> with standard output and error redirected to in-memory writers, then restores the previous console writers.
	/// </summary>
	/// <param name="runAsync">Typically <c>() => ArghRuntime.RunAsync(args)</c> or <c>() => ArghGenerated.RunAsync(args)</c>.</param>
	public static async Task<RunResult> RunWithCaptureAsync(Func<Task<int>> runAsync)
	{
		if (runAsync is null)
			throw new ArgumentNullException(nameof(runAsync));

		var stdout = new StringWriter();
		var stderr = new StringWriter();
		var prevOut = Console.Out;
		var prevErr = Console.Error;
		try
		{
			Console.SetOut(stdout);
			Console.SetError(stderr);
			var exitCode = await runAsync().ConfigureAwait(false);
			return new RunResult(exitCode, stdout.ToString(), stderr.ToString());
		}
		finally
		{
			Console.SetOut(prevOut);
			Console.SetError(prevErr);
		}
	}

	/// <summary>
	/// Runs <paramref name="runner"/> with the given arguments and standard streams captured.
	/// </summary>
	/// <param name="args">Arguments passed to <paramref name="runner"/>.</param>
	/// <param name="runner">Typically <see cref="ArghRuntime.RunAsync"/> or <c>ArghGenerated.RunAsync</c>.</param>
	public static Task<RunResult> RunWithCaptureAsync(string[] args, Func<string[], Task<int>> runner)
	{
		if (args is null)
			throw new ArgumentNullException(nameof(args));
		if (runner is null)
			throw new ArgumentNullException(nameof(runner));
		return RunWithCaptureAsync(() => runner(args));
	}

	/// <summary>
	/// Parses <paramref name="commandLine"/> with minimal quote-aware splitting, then runs <paramref name="runner"/> with captured output.
	/// </summary>
	/// <param name="commandLine">Whitespace-separated tokens; substrings in double quotes are kept as single arguments.</param>
	/// <param name="runner">Typically <see cref="ArghRuntime.RunAsync"/> or <c>ArghGenerated.RunAsync</c>.</param>
	public static Task<RunResult> RunWithCaptureAsync(string commandLine, Func<string[], Task<int>> runner)
	{
		if (commandLine is null)
			throw new ArgumentNullException(nameof(commandLine));
		if (runner is null)
			throw new ArgumentNullException(nameof(runner));
		var args = SplitCommandLine(commandLine);
		return RunWithCaptureAsync(args, runner);
	}

	/// <summary>
	/// Same as <see cref="RunWithCaptureAsync(string, Func{string[], Task{int}})"/> — alias for callers who prefer the name <c>RunAsync</c>.
	/// </summary>
	public static Task<RunResult> RunAsync(string commandLine, Func<string[], Task<int>> runner) =>
		RunWithCaptureAsync(commandLine, runner);

	/// <summary>
	/// Splits <paramref name="commandLine"/> on whitespace; characters inside a pair of double quotes are preserved as one token (quotes are not included in the token).
	/// </summary>
	public static string[] SplitCommandLine(string commandLine) => ParseArgs(commandLine);

	/// <summary>
	/// Splits <paramref name="commandLine"/> on whitespace; characters inside a pair of double quotes are preserved as one token (quotes are not included in the token).
	/// </summary>
	private static string[] ParseArgs(string commandLine)
	{
		if (string.IsNullOrWhiteSpace(commandLine))
			return Array.Empty<string>();

		var list = new List<string>();
		var sb = new StringBuilder();
		var inQuotes = false;
		for (var i = 0; i < commandLine.Length; i++)
		{
			var c = commandLine[i];
			if (c == '"')
			{
				inQuotes = !inQuotes;
				continue;
			}

			if (c == ' ' && !inQuotes)
			{
				if (sb.Length > 0)
				{
					list.Add(sb.ToString());
					sb.Clear();
				}

				continue;
			}

			sb.Append(c);
		}

		if (sb.Length > 0)
			list.Add(sb.ToString());

		return list.ToArray();
	}
}
