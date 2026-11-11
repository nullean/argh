using AwesomeAssertions;
using Nullean.Argh;

namespace Nullean.Make.Tests;

public record class Clean;

public record class Compile;

public record class Test(string Filter = "", int Retries = 0);

public record class Pack;

public record class Release;

public record class Migrate;

public record class Seed;

public union Db(Migrate, Seed);

public union Build(Clean, Compile, Test, Pack, Release, Db);

/// <summary>
/// <see cref="MakeApp{TUnion}"/> writes to <see cref="Console"/>, so these tests swap the process-wide
/// writers and must not run in parallel with each other.
/// </summary>
[NotInParallel]
public class MakeAppTests
{
	private sealed record Run(int ExitCode, IReadOnlyList<string> Log, string Stdout, string Stderr);

	/// <summary>
	/// Builds the app the way a build script would. Bodies append to <c>log</c>; <c>config</c> and
	/// <c>dryRun</c> are the global options the bodies read.
	/// </summary>
	private static async Task<Run> RunAsync(params string[] args)
	{
		var log = new List<string>();
		var app = new MakeApp<Build>("build", "Test build script");
		var config = app.Option<string>("--config", "Build configuration", defaultValue: "Release");
		var retries = app.Option<int>("--max-retries", "Retry budget", defaultValue: 1);
		var dryRun = app.Flag("--dry-run", "-n", "Print, do not run");

		app.Bind(
			b =>
				b.Value switch
				{
					Clean => app.Target().Description("Delete artifacts").Executes(() => log.Add("clean")),
					Compile => app.Target().DependsOn<Clean>().Executes(() => log.Add($"compile:{config.Value}:dry={dryRun.Value}:retries={retries.Value}")),
					Test t => app.Target().DependsOn(new Compile()).Executes(() => log.Add($"test:{t.Filter}:{t.Retries}")),
					Pack => app.Target().DependsOn<Test>().Executes(() => log.Add("pack")),
					Release => app.Command().Composes(new Pack(), new Db(new Migrate())).Executes(() => log.Add("release")),
					Db { Value: Migrate } => app.Target().Executes(() => log.Add("db-migrate")),
					Db { Value: Seed } => app.Target().DependsOn<Migrate>().Executes(() => log.Add("db-seed")),
					_ => throw new InvalidOperationException($"unmapped case {b.Value}")
				}
		);

		var stdout = new StringWriter();
		var stderr = new StringWriter();
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
	public async Task Target_runs_its_dependencies_first()
	{
		var run = await RunAsync("pack");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("clean", "compile:Release:dry=False:retries=1", "test::0", "pack");
	}

	[Test]
	public async Task Single_target_skips_dependencies()
	{
		var run = await RunAsync("pack", "-s");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("pack");
	}

	[Test]
	public async Task Target_flags_bind_to_the_case_record()
	{
		var run = await RunAsync("test", "--filter", "Unit", "--retries", "3");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().EndWith("test:Unit:3");
	}

	[Test]
	public async Task Global_options_are_visible_to_every_target()
	{
		var run = await RunAsync("compile", "--config", "Debug", "--dry-run", "--max-retries", "5");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("clean", "compile:Debug:dry=True:retries=5");
	}

	[Test]
	public async Task Short_global_flag_is_recognised()
	{
		var run = await RunAsync("compile", "-n", "-s");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("compile:Release:dry=True:retries=1");
	}

	[Test]
	public async Task Nested_union_cases_route_through_a_namespace()
	{
		var run = await RunAsync("db", "seed");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("db-migrate", "db-seed");
	}

	[Test]
	public async Task Command_composes_targets_including_nested_cases()
	{
		var run = await RunAsync("release");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Contain(new[] { "pack", "db-migrate", "release" });
		run.Log.ToList().IndexOf("release").Should().BeGreaterThan(run.Log.ToList().IndexOf("pack"));
	}

	[Test]
	public async Task Unknown_target_exits_2()
	{
		var run = await RunAsync("deploy");
		run.ExitCode.Should().Be(2);
		run.Stderr.Should().Contain("Unknown target 'deploy'.");
		run.Log.Should().BeEmpty();
	}

	[Test]
	public async Task Unknown_target_flag_exits_2_with_a_suggestion()
	{
		var run = await RunAsync("test", "--filtr", "Unit", "-s");
		run.ExitCode.Should().Be(2);
		run.Stderr.Should().Contain("Unknown flag '--filtr'.");
		run.Stderr.Should().Contain("--filter");
	}

	[Test]
	public async Task Unparseable_global_option_value_exits_2()
	{
		var run = await RunAsync("compile", "--max-retries", "lots");
		run.ExitCode.Should().Be(2);
		run.Stderr.Should().Contain("Cannot parse 'lots' for '--max-retries'.");
		run.Log.Should().BeEmpty();
	}

	[Test]
	public async Task Global_option_without_a_value_exits_2()
	{
		var run = await RunAsync("compile", "--config");
		run.ExitCode.Should().Be(2);
		run.Stderr.Should().Contain("Missing value for '--config'.");
		run.Log.Should().BeEmpty();
	}

	[Test]
	public async Task Root_help_lists_targets_and_global_options()
	{
		var run = await RunAsync("--help");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Stdout.Should().Contain("clean").And.Contain("Delete artifacts");
		run.Stdout.Should().Contain("Namespaces:").And.Contain("db");
		run.Stdout.Should().Contain("release").And.Contain("pack, db migrate");
		run.Stdout.Should().Contain("--config <string>").And.Contain("--dry-run");
		run.Stdout.Should().Contain("--max-retries <int>");
		run.Log.Should().BeEmpty();
	}

	[Test]
	public async Task Namespace_help_lists_nested_union_cases()
	{
		var run = await RunAsync("db", "--help");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Stdout.Should().Contain("migrate").And.Contain("seed");
		run.Stdout.Should().NotContain("Delete artifacts");
		run.Log.Should().BeEmpty();
	}

	[Test]
	public async Task Target_help_lists_its_flags()
	{
		var run = await RunAsync("test", "--help");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Stdout.Should().Contain("--filter").And.Contain("--retries");
		run.Log.Should().BeEmpty();
	}

	[Test]
	public async Task Dependency_on_a_type_that_is_not_a_case_fails_before_running()
	{
		var ran = false;
		var app = new MakeApp<Build>("build");
		app.Bind(
			b =>
				b.Value switch
				{
					Clean => app.Target().DependsOn<string>().Executes(() => ran = true),
					_ => app.Target()
				}
		);
		var stderr = new StringWriter();
		var oldErr = Console.Error;
		Console.SetError(stderr);
		try
		{
			(await app.RunAsync(["clean"])).Should().Be(1);
		}
		finally
		{
			Console.SetError(oldErr);
		}
		stderr.ToString().Should().Contain("Target 'clean' depends on 'String', which is not a case of Build.");
		ran.Should().BeFalse();
	}

	[Test]
	public async Task Bind_is_required()
	{
		var app = new MakeApp<Build>("build");
		var stderr = new StringWriter();
		var oldErr = Console.Error;
		Console.SetError(stderr);
		try
		{
			(await app.RunAsync(["clean"])).Should().Be(1);
		}
		finally
		{
			Console.SetError(oldErr);
		}
		stderr.ToString().Should().Contain("Bind() was not called.");
	}
}
