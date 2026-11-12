using AwesomeAssertions;

namespace Nullean.Make.Tests;

public record class Lint;

public record class Package;

public record class Ci;

public record class StartDb;

public record class StopDb;

public record class Integration(bool Fail = false);

public record class Push;

public record class Optional;

public record class Uses;

public record class Loop1;

public record class Loop2;

public record class Loops;

public union Modified(Lint, Package, Ci, StartDb, StopDb, Integration, Push, Optional, Uses, Loop1, Loop2, Loops);

/// <summary>OnlyWhen, After, Cleanup, NeedsEnv and NeedsTool on a <see cref="MakeApp{TUnion}"/>.</summary>
[NotInParallel]
public class ModifierTests
{
	internal const string Variable = "NULLEAN_MAKE_TEST_PUSH_KEY";

	private sealed record Run(int ExitCode, IReadOnlyList<string> Log, string Stdout, string Stderr);

	private static async Task<Run> RunAsync(params string[] args)
	{
		var log = new List<string>();
		var app = new MakeApp<Modified>("build");
		var skipOptional = app.Flag("--skip-optional");
		app.Bind(
			m =>
				m.Value switch
				{
					Lint => app.Target(() => log.Add("lint")),
					Package => app.Target(() => log.Add("package")).After(new Lint()),
					// A parallel stage leaves the two unordered; After orders them.
					Ci => app.Command().Parallel(new Package(), new Lint()),
					StartDb => app.Target(() => log.Add("start-db")).Cleanup(new StopDb()),
					StopDb => app.Target(() => log.Add("stop-db")),
					Integration i =>
						app.Target([new StartDb()], () =>
						{
							log.Add("integration");
							if (i.Fail)
								throw new MakeException("integration failed", 4);
						}),
					Push => app.Target(() => log.Add("push")).NeedsEnv(Variable).NeedsTool("dotnet"),
					Optional => app.Target([new Lint()], () => log.Add("optional")).OnlyWhen(() => !skipOptional.Value),
					Uses => app.Target(() => log.Add("uses")).NeedsTool("nullean-make-no-such-tool").NeedsEnv(Variable),
					Loop1 => app.Target(() => log.Add("loop1")).After(new Loop2()),
					Loop2 => app.Target(() => log.Add("loop2")).After(new Loop1()),
					Loops => app.Command([], [new Loop1(), new Loop2()]),
					_ => throw new InvalidOperationException($"unmapped case {m.Value}")
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
	public async Task A_false_condition_skips_the_body_but_not_the_dependencies()
	{
		var run = await RunAsync("optional", "--skip-optional");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("lint");
	}

	[Test]
	public async Task Graph_shows_what_a_condition_skips()
	{
		var run = await RunAsync("optional", "--skip-optional", "--graph");
		run.Stdout.Should().Contain("optional  skipped: its condition is false");
	}

	[Test]
	public async Task After_orders_a_target_after_one_in_the_same_run()
	{
		var run = await RunAsync("ci", "-j", "4");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("lint", "package");
	}

	[Test]
	public async Task After_does_not_add_the_target_to_the_run()
	{
		var run = await RunAsync("package");
		run.Log.Should().Equal("package");
	}

	[Test]
	public async Task After_that_forms_a_cycle_exits_1()
	{
		var run = await RunAsync("loops");
		run.ExitCode.Should().Be(1);
		run.Stderr.Should().Contain("has a cycle");
		run.Log.Should().BeEmpty();
	}

	[Test]
	public async Task A_cleanup_runs_at_the_end()
	{
		var run = await RunAsync("integration");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("start-db", "integration", "stop-db");
	}

	[Test]
	public async Task A_cleanup_runs_after_a_failure_and_the_run_keeps_the_failures_exit_code()
	{
		var run = await RunAsync("integration", "--fail");
		run.ExitCode.Should().Be(4);
		run.Log.Should().Equal("start-db", "integration", "stop-db");
	}

	[Test]
	public async Task Graph_shows_the_cleanup()
	{
		var run = await RunAsync("integration", "--graph");
		run.Stdout.Should().Contain("cleanup, at the end").And.Contain("then cleanup: stop-db");
	}

	[Test]
	public async Task A_missing_environment_variable_stops_the_run_before_anything_starts()
	{
		Environment.SetEnvironmentVariable(Variable, null);
		var run = await RunAsync("push");
		run.ExitCode.Should().Be(1);
		run.Log.Should().BeEmpty();
		run.Stderr.Should().Contain($"the environment variable {Variable}, for push");
	}

	[Test]
	public async Task A_set_environment_variable_and_a_tool_on_the_path_let_the_run_start()
	{
		Environment.SetEnvironmentVariable(Variable, "set");
		try
		{
			var run = await RunAsync("push");
			run.ExitCode.Should().Be(0);
			run.Log.Should().Equal("push");
		}
		finally
		{
			Environment.SetEnvironmentVariable(Variable, null);
		}
	}

	[Test]
	public async Task Every_missing_precondition_is_listed()
	{
		Environment.SetEnvironmentVariable(Variable, null);
		var run = await RunAsync("uses");
		run.ExitCode.Should().Be(1);
		run.Stderr.Should().Contain("nullean-make-no-such-tool on the PATH, for uses").And.Contain($"the environment variable {Variable}, for uses");
	}

	[Test]
	public async Task Graph_lists_what_is_missing_and_runs_nothing()
	{
		Environment.SetEnvironmentVariable(Variable, null);
		var run = await RunAsync("uses", "--graph");
		run.ExitCode.Should().Be(0);
		run.Stdout.Should().Contain("Missing: nullean-make-no-such-tool on the PATH, for uses");
	}
}

public class ModifiedBuild : MakeBuild
{
	public static List<string> Log { get; } = new();

	public Target Lint => _ => _.Executes(() => Log.Add("lint"));

	public Target Package => _ => _.After(Lint).Executes(() => Log.Add("package"));

	public Command Ci => _ => _.Parallel(Package, Lint);

	public Target StopDb => _ => _.Executes(() => Log.Add("stop-db"));

	public Target StartDb => _ => _.Cleanup(StopDb).Executes(() => Log.Add("start-db"));

	public Target Integration => _ => _.DependsOn(StartDb).Executes(() => throw new MakeException("integration failed", 4));

	public Target Push => _ => _.NeedsEnv(ModifierTests.Variable).Executes(() => Log.Add("push"));
}

[NotInParallel]
public class ClassModifierTests
{
	private static async Task<(int Exit, List<string> Log, string Stderr)> RunAsync(params string[] args)
	{
		ModifiedBuild.Log.Clear();
		var stderr = new StringWriter();
		var (oldOut, oldErr) = (Console.Out, Console.Error);
		Console.SetOut(new StringWriter());
		Console.SetError(stderr);
		try
		{
			return (await MakeApp.Execute<ModifiedBuild>(args), [.. ModifiedBuild.Log], stderr.ToString());
		}
		finally
		{
			Console.SetOut(oldOut);
			Console.SetError(oldErr);
		}
	}

	[Test]
	public async Task After_orders_a_target_after_one_in_the_same_run()
	{
		var (exit, log, _) = await RunAsync("ci");
		exit.Should().Be(0);
		log.Should().Equal("lint", "package");
	}

	[Test]
	public async Task A_cleanup_runs_after_a_failure()
	{
		var (exit, log, _) = await RunAsync("integration");
		exit.Should().Be(4);
		log.Should().Equal("start-db", "stop-db");
	}

	[Test]
	public async Task A_missing_environment_variable_stops_the_run()
	{
		Environment.SetEnvironmentVariable(ModifierTests.Variable, null);
		var (exit, log, stderr) = await RunAsync("push");
		exit.Should().Be(1);
		log.Should().BeEmpty();
		stderr.Should().Contain(ModifierTests.Variable);
	}
}
