using System.Text.Json;
using AwesomeAssertions;

namespace Nullean.Make.Tests;

public record class Wipe;

public record class Compose;

public record class Probe(string Filter = "");

public record class Ship;

public union Drawn(Wipe, Compose, Probe, Ship);

/// <summary><c>--graph</c> draws the plan the same run would execute, and runs nothing.</summary>
[NotInParallel]
public class GraphTests
{
	private sealed record Run(int ExitCode, IReadOnlyList<string> Log, string Stdout, string Stderr);

	private static async Task<Run> RunAsync(params string[] args)
	{
		var log = new List<string>();
		var app = new MakeApp<Drawn>("build");
		app.Bind(
			d =>
				d.Value switch
				{
					Wipe => app.Target(() => log.Add("wipe")),
					Compose => app.Target([new Wipe()], () => log.Add("compose")),
					Probe p => app.Target([new Compose()], () => log.Add($"probe:{p.Filter}")),
					Ship => app.Command([new Probe("Unit")], [new Wipe()]),
					_ => throw new InvalidOperationException($"unmapped case {d.Value}")
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
	public async Task Graph_draws_the_plan_and_runs_nothing()
	{
		var run = await RunAsync("ship", "--graph");
		run.ExitCode.Should().Be(0);
		run.Log.Should().BeEmpty();
		run.Stdout.Should().Contain("├─ requires").And.Contain("└─ composes").And.Contain("probe (filter: Unit)");
		run.Stdout.Should().Contain("Runs: wipe → compose → probe (filter: Unit)");
	}

	[Test]
	public async Task Graph_shows_a_gate_that_single_target_skips()
	{
		var run = await RunAsync("ship", "-s", "--graph");
		run.Stdout.Should().Contain("probe (filter: Unit)  skipped: -s");
		run.Stdout.Should().Contain("Runs: wipe");
		run.Stdout.Should().NotContain("compose" + Environment.NewLine);
	}

	[Test]
	public async Task Graph_binds_the_named_targets_arguments()
	{
		var run = await RunAsync("probe", "--filter", "Smoke", "--graph");
		run.Stdout.Should().Contain("probe (filter: Smoke)");
	}

	[Test]
	public async Task Graph_mermaid_draws_one_edge_per_ordering()
	{
		var run = await RunAsync("probe", "--graph", "mermaid");
		run.ExitCode.Should().Be(0);
		var lines = run.Stdout.Split('\n', StringSplitOptions.TrimEntries);
		lines.Should().Contain("flowchart TD");
		lines.Count(l => l.Contains("-->")).Should().Be(2);
	}

	[Test]
	public async Task Graph_json_lists_the_targets_in_run_order()
	{
		var run = await RunAsync("ship", "--graph=json");
		run.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(run.Stdout);
		var names = doc.RootElement.GetProperty("targets").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
		names.Should().Equal("wipe", "compose", "probe (filter: Unit)", "ship");
		doc.RootElement.GetProperty("roots")[0].GetString().Should().Be("ship");
	}

	[Test]
	public async Task Graph_without_a_target_draws_every_target_nothing_depends_on()
	{
		var run = await RunAsync("--graph");
		run.ExitCode.Should().Be(0);
		// Everything else is a dependency of ship, so ship is the one root.
		run.Stdout.Should().StartWith("ship");
		run.Log.Should().BeEmpty();
	}

	[Test]
	public async Task An_unknown_graph_format_exits_2()
	{
		var run = await RunAsync("ship", "--graph=dot");
		run.ExitCode.Should().Be(2);
		run.Stderr.Should().Contain("Unknown graph format 'dot'");
	}

	[Test]
	public async Task Target_help_lists_what_it_runs()
	{
		var run = await RunAsync("ship", "--help");
		run.Stdout.Should().Contain("Runs:").And.Contain("wipe → compose → probe (filter: Unit)");
		run.Stdout.Should().Contain("--graph [<format>]");
	}
}

public class ConditionalBuild : MakeBuild
{
	[Flag("--skip-lint", Description = "Skip lint")]
	public bool SkipLint { get; init; }

	public Target Lint =>
		_ => _.OnlyWhen(() => !SkipLint).Executes(() =>
		{ });

	public Target Compile =>
		_ => _.DependsOn(Lint).Executes(() =>
		{ });
}

[NotInParallel]
public class ClassGraphTests
{
	private static async Task<(int Exit, string Stdout)> RunAsync(params string[] args)
	{
		var stdout = new StringWriter();
		var (oldOut, oldErr) = (Console.Out, Console.Error);
		Console.SetOut(stdout);
		Console.SetError(new StringWriter());
		try
		{
			return (await MakeApp.Execute<ConditionalBuild>(args), stdout.ToString());
		}
		finally
		{
			Console.SetOut(oldOut);
			Console.SetError(oldErr);
		}
	}

	[Test]
	public async Task Graph_evaluates_conditions_and_shows_what_they_skip()
	{
		var (exit, stdout) = await RunAsync("compile", "--skip-lint", "--graph");
		exit.Should().Be(0);
		stdout.Should().Contain("lint  skipped: its condition is false");
		stdout.Should().Contain("Runs: compile");
	}

	[Test]
	public async Task Graph_shows_a_condition_that_holds_as_running()
	{
		var (_, stdout) = await RunAsync("compile", "--graph");
		stdout.Should().NotContain("skipped");
		stdout.Should().Contain("Runs: lint → compile");
	}
}
