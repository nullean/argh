using AwesomeAssertions;

namespace Nullean.Make.Tests;

public record class Suite(string Filter = "");

public record class Gate(string Filter = "");

public record class Split;

public record class Twice;

public union Valued(Suite, Gate, Split, Twice);

/// <summary>A dependency's value is part of its identity: equal values run once, different values once each.</summary>
[NotInParallel]
public class DependencyValueTests
{
	private sealed record Run(int ExitCode, IReadOnlyList<string> Log, string Stdout, string Stderr);

	private static async Task<Run> RunAsync(params string[] args)
	{
		var log = new List<string>();
		var app = new MakeApp<Valued>("build");
		app.Bind(
			v =>
				v.Value switch
				{
					Suite s => app.Target(() => log.Add($"suite:{s.Filter}")),
					// Gate takes the same --filter as Suite; its dependency on Suite names no filter.
					Gate g => app.Target([new Suite()], () => log.Add($"gate:{g.Filter}")),
					Split => app.Command([], [new Suite("Unit"), new Suite("Integration")]),
					Twice => app.Command([], [new Suite("Unit"), new Suite("Unit")]),
					_ => throw new InvalidOperationException($"unmapped case {v.Value}")
				}
		);
		var (stdout, stderr) = (new StringWriter(), new StringWriter());
		var (oldOut, oldErr) = (Console.Out, Console.Error);
		Console.SetOut(stdout);
		Console.SetError(stderr);
		try
		{
			var exit = await app.RunAsync(args);
			return new Run(exit, log, stdout.ToString(), stderr.ToString());
		}
		finally
		{
			Console.SetOut(oldOut);
			Console.SetError(oldErr);
		}
	}

	[Test]
	public async Task Two_references_with_different_values_run_the_target_once_each()
	{
		var run = await RunAsync("split");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("suite:Unit", "suite:Integration");
	}

	[Test]
	public async Task Two_references_with_equal_values_run_the_target_once()
	{
		var run = await RunAsync("twice");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("suite:Unit");
	}

	[Test]
	public async Task The_named_targets_arguments_do_not_reach_its_dependencies()
	{
		var run = await RunAsync("gate", "--filter", "Smoke");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("suite:", "gate:Smoke");
	}

	[Test]
	public async Task Version_prints_the_informational_version_of_the_entry_assembly()
	{
		var run = await RunAsync("--version");
		var expected = System
			.Reflection
			.CustomAttributeExtensions
			.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(System.Reflection.Assembly.GetEntryAssembly()!)!.InformationalVersion;
		run.Stdout.Trim().Should().Be(expected).And.NotBe("0.0.0");
	}

	[Test]
	public async Task The_log_names_a_dependency_by_its_value()
	{
		var run = await RunAsync("split");
		run.Stdout.Should().Contain("suite (filter: Unit)").And.Contain("suite (filter: Integration)");
	}
}

public record class SuiteOptions(string Filter = "");

public class ValuedBuild : MakeBuild
{
	public static List<string> Log { get; } = new();

	public Target<SuiteOptions> Suite => _ => _.Executes(o => Log.Add($"suite:{o.Filter}"));

	public Command Split => _ => _.Composes(Suite.With(new("Unit")), Suite.With(new("Integration")));

	public Command Twice => _ => _.Composes(Suite.With(new("Unit")), Suite.With(new("Unit")));

	public Target<SuiteOptions> Gate => _ => _.DependsOn(Suite).Executes(o => Log.Add($"gate:{o.Filter}"));
}

[NotInParallel]
public class ClassDependencyValueTests
{
	private static async Task<(int Exit, List<string> Log)> RunAsync(params string[] args)
	{
		ValuedBuild.Log.Clear();
		var (oldOut, oldErr) = (Console.Out, Console.Error);
		Console.SetOut(new StringWriter());
		Console.SetError(new StringWriter());
		try
		{
			var exit = await MakeApp.Execute<ValuedBuild>(args);
			return (exit, [.. ValuedBuild.Log]);
		}
		finally
		{
			Console.SetOut(oldOut);
			Console.SetError(oldErr);
		}
	}

	[Test]
	public async Task With_runs_the_target_with_that_value()
	{
		var (exit, log) = await RunAsync("split");
		exit.Should().Be(0);
		log.Should().Equal("suite:Unit", "suite:Integration");
	}

	[Test]
	public async Task Equal_values_run_once()
	{
		var (exit, log) = await RunAsync("twice");
		exit.Should().Be(0);
		log.Should().Equal("suite:Unit");
	}

	[Test]
	public async Task The_named_targets_arguments_do_not_reach_its_dependencies()
	{
		var (exit, log) = await RunAsync("gate", "--filter", "Smoke");
		exit.Should().Be(0);
		log.Should().Equal("suite:", "gate:Smoke");
	}
}
