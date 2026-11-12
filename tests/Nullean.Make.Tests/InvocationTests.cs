using AwesomeAssertions;
using Nullean.Argh;

namespace Nullean.Make.Tests;

public record class Restore;

public record class Assemble;

public record class Verify(string Suite = "all", bool Quick = false);

public record class Bundle;

public record class Format([Argument] string Path = ".");

public record class Shipit;

public record class Sweep;

public union Called(Restore, Assemble, Verify, Bundle, Format, Shipit, Sweep);

/// <summary>Several targets on one command line, and <c>--skip</c>.</summary>
[NotInParallel]
public class InvocationTests
{
	private sealed record Run(int ExitCode, IReadOnlyList<string> Log, string Stdout, string Stderr);

	private static async Task<Run> RunAsync(params string[] args)
	{
		var log = new List<string>();
		var app = new MakeApp<Called>("build");
		app.Bind(
			c =>
				c.Value switch
				{
					Restore => app.Target(() => log.Add("restore")),
					Assemble => app.Target([new Restore()], () => log.Add("assemble")),
					Verify v => app.Target([new Assemble()], () => log.Add($"verify:{v.Suite}:{v.Quick}")),
					Bundle => app.Target([new Assemble()], () => log.Add("bundle")),
					Format f => app.Target(() => log.Add($"format:{f.Path}")),
					Shipit => app.Command([new Verify()], [new Bundle()]),
					Sweep => app.Target(() => log.Add("sweep")),
					_ => throw new InvalidOperationException($"unmapped case {c.Value}")
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
	public async Task Several_targets_run_in_order_and_share_their_dependencies()
	{
		var run = await RunAsync("sweep", "verify", "bundle");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("sweep", "restore", "assemble", "verify:all:False", "bundle");
	}

	[Test]
	public async Task Each_target_binds_the_arguments_up_to_the_next_one()
	{
		var run = await RunAsync("verify", "--suite", "unit", "--quick", "bundle");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("restore", "assemble", "verify:unit:True", "bundle");
	}

	[Test]
	public async Task An_option_value_that_names_a_target_stays_with_the_option()
	{
		var run = await RunAsync("verify", "--suite", "bundle");
		run.Log.Should().Equal("restore", "assemble", "verify:bundle:False");
	}

	[Test]
	public async Task A_target_with_positional_arguments_takes_the_words_after_it()
	{
		var run = await RunAsync("format", "sweep");
		run.Log.Should().Equal("format:sweep");
	}

	[Test]
	public async Task Skip_leaves_a_target_and_what_only_it_needed_out_of_the_plan()
	{
		var run = await RunAsync("shipit", "--skip", "verify");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("restore", "assemble", "bundle");
	}

	[Test]
	public async Task Graph_shows_what_skip_leaves_out()
	{
		var run = await RunAsync("shipit", "--skip=verify", "--graph");
		run.Stdout.Should().Contain("verify  skipped: --skip");
	}

	[Test]
	public async Task Skip_of_an_unknown_target_exits_2_with_a_suggestion()
	{
		var run = await RunAsync("shipit", "--skip", "verfy");
		run.ExitCode.Should().Be(2);
		run.Stderr.Should().Contain("Unknown target 'verfy' for '--skip'. Did you mean 'verify'?");
		run.Log.Should().BeEmpty();
	}

	[Test]
	public async Task Skip_of_a_target_the_command_line_runs_exits_2()
	{
		var run = await RunAsync("shipit", "--skip", "shipit");
		run.ExitCode.Should().Be(2);
		run.Log.Should().BeEmpty();
	}
}
