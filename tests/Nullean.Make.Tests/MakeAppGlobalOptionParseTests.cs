using AwesomeAssertions;
using Nullean.Argh;

namespace Nullean.Make.Tests;

public enum ParseTestLevel { Low, High }

public class GlobalOptionParseBuild : MakeBuild
{
	internal static readonly List<string> Log = new();

	[GlobalOption("--parallel", Description = "Worker count")]
	public int Parallel { get; set; } = 1;

	[GlobalOption("--ratio", Description = "Sampling ratio")]
	public double Ratio { get; set; } = 1;

	[GlobalOption("--level", Description = "Level")]
	public ParseTestLevel Level { get; set; }

	public Target Run => _ => _.Executes(() => Log.Add($"run:{Parallel}:{Ratio}:{Level}"));
}

[NotInParallel]
public class MakeAppGlobalOptionParseTests
{
	private static async Task<(int ExitCode, string Stderr)> RunAsync(params string[] args)
	{
		GlobalOptionParseBuild.Log.Clear();
		var stderr = new StringWriter();
		var (oldOut, oldErr) = (Console.Out, Console.Error);
		Console.SetOut(TextWriter.Null);
		Console.SetError(stderr);
		try
		{
			var exit = await MakeApp.Execute<GlobalOptionParseBuild>(args);
			return (exit, stderr.ToString());
		}
		finally
		{
			Console.SetOut(oldOut);
			Console.SetError(oldErr);
		}
	}

	[Test]
	public async Task Typed_global_options_bind()
	{
		var (exit, stderr) = await RunAsync("run", "--parallel", "4", "--ratio", "0.5", "--level", "high");
		exit.Should().Be(0, stderr);
		GlobalOptionParseBuild.Log.Should().Equal("run:4:0.5:High");
	}

	[Test]
	[Arguments("--parallel", "lots")]
	[Arguments("--ratio", "half")]
	[Arguments("--level", "extreme")]
	public async Task Unparseable_global_option_value_exits_2_without_running(string flag, string value)
	{
		var (exit, stderr) = await RunAsync("run", flag, value);
		exit.Should().Be(2);
		stderr.Should().Contain($"Cannot parse '{value}' for '{flag}'.");
		GlobalOptionParseBuild.Log.Should().BeEmpty();
	}

	[Test]
	public async Task Global_option_without_a_value_exits_2()
	{
		var (exit, stderr) = await RunAsync("run", "--parallel");
		exit.Should().Be(2);
		stderr.Should().Contain("Missing value for '--parallel'.");
		GlobalOptionParseBuild.Log.Should().BeEmpty();
	}
}
