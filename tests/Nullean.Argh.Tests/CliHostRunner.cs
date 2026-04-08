using ProcNet;
using ProcNet.Std;

namespace Nullean.Argh.Tests;

/// <summary>Runs the Nullean.Argh.Tests.CliHost build output via <c>dotnet exec</c> (integration tests).</summary>
internal static class CliHostRunner
{
	private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(2);

	internal static ProcessCaptureResult Run(params string[] cliArgs)
	{
		var dll = Path.Combine(AppContext.BaseDirectory, "Nullean.Argh.Tests.CliHost.dll");
		if (!File.Exists(dll))
			throw new InvalidOperationException($"CliHost not found next to tests: {dll}. Build Nullean.Argh.Tests.CliHost first.");

		var allArgs = new string[2 + cliArgs.Length];
		allArgs[0] = "exec";
		allArgs[1] = dll;
		for (var i = 0; i < cliArgs.Length; i++)
			allArgs[2 + i] = cliArgs[i];

		var start = new StartArguments("dotnet", allArgs) { Timeout = s_timeout };
		return Proc.Start(start);
	}

	internal static string StdoutText(ProcessCaptureResult result) =>
		string.Concat(result.ConsoleOut.Cast<LineOut>().Where(l => !l.Error).Select(l => l.Line));

	internal static string StderrText(ProcessCaptureResult result) =>
		string.Concat(result.ConsoleOut.Cast<LineOut>().Where(l => l.Error).Select(l => l.Line));
}
