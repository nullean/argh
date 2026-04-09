using System.Collections;
using ProcNet;
using ProcNet.Std;

namespace Nullean.Argh.IntegrationTests.Infrastructure;

/// <summary>Runs the <see cref="CliHostPaths.CliHostDllFileName"/> build output via <c>dotnet exec</c>.</summary>
/// <remarks>Uses <see cref="Proc.Start(StartArguments)"/> exclusively; optional environment overrides merge with the current process environment.</remarks>
internal static class CliHostRunner
{
	private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(2);

	internal static ProcessCaptureResult Run(params string[] cliArgs) =>
		Run(environment: null, cliArgs);

	/// <param name="environment">Extra or replacement variables merged on top of <see cref="Environment.GetEnvironmentVariables()"/>.</param>
	/// <param name="cliArgs">Arguments after <c>dotnet exec</c> and the host DLL.</param>
	internal static ProcessCaptureResult Run(IReadOnlyDictionary<string, string>? environment, params string[] cliArgs)
	{
		var dll = Path.Combine(AppContext.BaseDirectory, CliHostPaths.CliHostDllFileName);
		if (!File.Exists(dll))
			throw new InvalidOperationException($"CliHost not found next to tests: {dll}. Build Nullean.Argh.Tests.CliHost first.");

		var allArgs = new string[2 + cliArgs.Length];
		allArgs[0] = "exec";
		allArgs[1] = dll;
		for (var i = 0; i < cliArgs.Length; i++)
			allArgs[2 + i] = cliArgs[i];

		var start = new StartArguments("dotnet", allArgs) { Timeout = s_timeout };
		if (environment is { Count: > 0 })
			start.Environment = MergeProcessEnvironment(environment);

		return Proc.Start(start);
	}

	private static Dictionary<string, string> MergeProcessEnvironment(IReadOnlyDictionary<string, string> overrides)
	{
		var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
		{
			if (e.Key is string k && e.Value is string v)
				d[k] = v;
		}

		foreach (var kv in overrides)
			d[kv.Key] = kv.Value;

		return d;
	}

	internal static string StdoutText(ProcessCaptureResult result)
	{
		var lines = result.ConsoleOut.Cast<LineOut>().Where(l => !l.Error).Select(l => l.Line).ToArray();
		return lines.Length == 0 ? "" : string.Join("\n", lines) + "\n";
	}

	internal static string StderrText(ProcessCaptureResult result)
	{
		var lines = result.ConsoleOut.Cast<LineOut>().Where(l => l.Error).Select(l => l.Line).ToArray();
		return lines.Length == 0 ? "" : string.Join("\n", lines) + "\n";
	}
}
