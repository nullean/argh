using System.Collections;
using ProcNet;
using ProcNet.Std;
using Xunit;

namespace Argh.ExternalNs.GlobalOptions.Repro.Tests;

/// <summary>
/// Regression tests: UseGlobalOptions&lt;T&gt; where T is from an external assembly whose namespace
/// (External.Ns.Options) does not share a prefix with the consuming project's RootNamespace
/// (Documentation.Builder).
///
/// Compile-time check: If the generator emits the wrong FQ name
/// (global::Documentation.Builder.ExternalNsGlobalOptions instead of
/// global::External.Ns.Options.ExternalNsGlobalOptions), the CliApp project fails to compile,
/// which causes this test project's build to fail before any test runs.
///
/// Behavioral checks: The echo command injects global options and prints their values.
/// </summary>
public class ExternalNsGlobalOptionsTests
{
	private static ProcessCaptureResult Run(params string[] args)
	{
		var dll = Path.Combine(AppContext.BaseDirectory, "Argh.ExternalNs.GlobalOptions.Repro.CliApp.dll");
		if (!File.Exists(dll))
			throw new InvalidOperationException($"CliApp not found: {dll}. Build the CliApp project first.");
		var allArgs = new string[2 + args.Length];
		allArgs[0] = "exec";
		allArgs[1] = dll;
		for (var i = 0; i < args.Length; i++)
			allArgs[2 + i] = args[i];
		return Proc.Start(new StartArguments("dotnet", allArgs) { Timeout = TimeSpan.FromSeconds(30) });
	}

	private static string Stdout(ProcessCaptureResult r) =>
		string.Join("\n", r.ConsoleOut.Cast<LineOut>().Where(l => !l.Error).Select(l => l.Line)).Trim();

	[Fact]
	public void External_ns_global_options_defaults_used_when_flags_absent()
	{
		var result = Run("echo");
		Assert.Equal(0, result.ExitCode);
		Assert.Equal("verbose=False tag=default", Stdout(result));
	}

	[Fact]
	public void External_ns_global_options_verbose_flag_parsed()
	{
		var result = Run("echo", "--verbose");
		Assert.Equal(0, result.ExitCode);
		Assert.Equal("verbose=True tag=default", Stdout(result));
	}

	[Fact]
	public void External_ns_global_options_tag_flag_parsed()
	{
		var result = Run("echo", "--tag", "custom");
		Assert.Equal(0, result.ExitCode);
		Assert.Equal("verbose=False tag=custom", Stdout(result));
	}
}
