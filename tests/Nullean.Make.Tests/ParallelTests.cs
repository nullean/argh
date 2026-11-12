using System.Collections.Concurrent;
using AwesomeAssertions;
using static Nullean.Make.Shell;

namespace Nullean.Make.Tests;

public record class Left;

public record class Right;

public record class After;

public record class Broken;

public record class Slow;

public record class Both;

public record class Failing;

public record class Loud;

public record class Shelled;

public record class Gated;

public union Side(Left, Right, After, Broken, Slow, Both, Failing, Loud, Shelled, Gated);

/// <summary>Targets in a parallel stage run side by side; the stage after waits for all of them.</summary>
[NotInParallel]
public class ParallelTests
{
	private sealed record Run(int ExitCode, IReadOnlyList<string> Log, string Stdout, string Stderr, TimeSpan Elapsed);

	/// <summary>
	/// Left and Right each wait, up to a few seconds, for the other to start. Side by side they meet and log
	/// <c>met</c>; one after the other the first gives up and logs <c>alone</c>.
	/// </summary>
	private static async Task<Run> RunAsync(params string[] args)
	{
		var log = new ConcurrentQueue<string>();
		using var both = new CountdownEvent(2);

		void Meet(string name)
		{
			Console.WriteLine($"{name}-before");
			both.Signal();
			log.Enqueue(both.Wait(TimeSpan.FromSeconds(3)) ? $"{name}:met" : $"{name}:alone");
			Console.WriteLine($"{name}-after");
		}

		var app = new MakeApp<Side>("build");
		var gate = app.Flag("--gate", "-k", "A build's own -k");
		app.Bind(
			s =>
				s.Value switch
				{
					Left => app.Target(() => Meet("left")),
					Right => app.Target(() => Meet("right")),
					After => app.Target(() => log.Enqueue("after")),
					Broken => app.Target(() => throw new MakeException("broken on purpose", 3)),
					Slow =>
						app.Target(async ct =>
						{
							await Task.Delay(TimeSpan.FromSeconds(20), ct);
							log.Enqueue("slow:done");
						}),
					Both => app.Command().Parallel(new Left(), new Right()).Composes(new After()),
					Failing => app.Command().Parallel(new Slow(), new Broken()).Composes(new After()),
					Loud => app.Command().Parallel(new Left(), new Right()).Executes(() => Console.WriteLine("loud done")),
					Shelled => app.Command().Parallel(new Left(), new Right()),
					Gated => app.Target(() => log.Enqueue($"gate:{gate.Value}")),
					_ => throw new InvalidOperationException($"unmapped case {s.Value}")
				}
		);

		var (stdout, stderr) = (new StringWriter(), new StringWriter());
		var (oldOut, oldErr) = (Console.Out, Console.Error);
		Console.SetOut(stdout);
		Console.SetError(stderr);
		var watch = System.Diagnostics.Stopwatch.StartNew();
		try
		{
			var exit = await app.RunAsync(args);
			return new Run(exit, [.. log], stdout.ToString(), stderr.ToString(), watch.Elapsed);
		}
		finally
		{
			Console.SetOut(oldOut);
			Console.SetError(oldErr);
		}
	}

	[Test]
	public async Task A_parallel_stage_runs_its_targets_side_by_side()
	{
		var run = await RunAsync("both", "-j", "2");
		run.ExitCode.Should().Be(0);
		run.Log.Take(2).Should().BeEquivalentTo("left:met", "right:met");
		run.Log.Last().Should().Be("after");
	}

	[Test]
	public async Task One_job_runs_a_parallel_stage_one_target_at_a_time()
	{
		var run = await RunAsync("both", "--jobs=1");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("left:alone", "right:met", "after");
	}

	[Test]
	public async Task A_failure_cancels_the_targets_still_running_and_starts_no_more()
	{
		var run = await RunAsync("failing", "-j", "2");
		run.ExitCode.Should().Be(3);
		run.Stderr.Should().Contain("Target 'broken' failed: broken on purpose");
		run.Log.Should().NotContain("slow:done").And.NotContain("after");
		run.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
	}

	[Test]
	public async Task Keep_going_lets_the_targets_that_do_not_depend_on_a_failure_finish()
	{
		var run = await RunAsync("failing", "-j", "2", "--keep-going");
		run.ExitCode.Should().Be(3);
		run.Log.Should().Contain("slow:done").And.NotContain("after");
	}

	[Test]
	public async Task Output_of_targets_running_side_by_side_is_prefixed_with_their_names()
	{
		var run = await RunAsync("loud", "-j", "2");
		run.ExitCode.Should().Be(0);
		run.Stdout.Should().Contain("[loud] loud done");
		run.Stdout.Should().Contain("Took");
	}

	[Test]
	public async Task Prefixed_output_interleaves_the_lines_of_targets_running_side_by_side()
	{
		// Both targets write their first line before either writes its second, so prefixed lines interleave.
		var run = await RunAsync("both", "-j", "2");
		var lines = run.Stdout.Split(Environment.NewLine).Select(l => l.Trim()).ToList();
		var before = new[] { lines.IndexOf("[left] left-before"), lines.IndexOf("[right] right-before") };
		var after = new[] { lines.IndexOf("[left] left-after"), lines.IndexOf("[right] right-after") };
		before.Should().NotContain(-1);
		before.Max().Should().BeLessThan(after.Min());
	}

	[Test]
	public async Task Grouped_output_writes_a_targets_lines_together()
	{
		var run = await RunAsync("both", "-j", "2", "--output", "grouped");
		run.ExitCode.Should().Be(0);
		run.Log.Take(2).Should().BeEquivalentTo("left:met", "right:met");
		var lines = run.Stdout.Split(Environment.NewLine).Select(l => l.Trim()).ToList();
		lines.Should().Contain("left-before").And.NotContain(l => l.StartsWith("[left]"));
		lines.IndexOf("left-after").Should().Be(lines.IndexOf("left-before") + 1);
		lines.IndexOf("right-after").Should().Be(lines.IndexOf("right-before") + 1);
	}

	[Test]
	public async Task A_build_option_with_the_same_short_name_wins()
	{
		var run = await RunAsync("gated", "-k");
		run.ExitCode.Should().Be(0);
		run.Log.Should().Equal("gate:True");
	}

	[Test]
	public async Task Jobs_takes_a_number_greater_than_zero()
	{
		var run = await RunAsync("both", "-j", "0");
		run.ExitCode.Should().Be(2);
		run.Stderr.Should().Contain("'--jobs' takes a number of targets greater than 0, not '0'.");
	}

	[Test]
	public async Task Graph_draws_a_parallel_stage()
	{
		var run = await RunAsync("both", "--graph");
		run.Stdout.Should().Contain("parallel").And.Contain("Runs: left → right → after");
	}
}

public record class EchoA;

public record class EchoB;

public record class Echoes;

public union Echoing(EchoA, EchoB, Echoes);

[NotInParallel]
public class ParallelShellTests
{
	[Test]
	public async Task A_command_run_with_exec_goes_through_the_targets_prefixed_output()
	{
		var app = new MakeApp<Echoing>("build");
		app.Bind(
			e =>
				e.Value switch
				{
					EchoA => app.Target(() => Exec("dotnet", "--version")),
					EchoB => app.Target(() => Exec("dotnet", "--version")),
					Echoes => app.Command().Parallel(new EchoA(), new EchoB()),
					_ => throw new InvalidOperationException()
				}
		);
		var stdout = new StringWriter();
		var (oldOut, oldErr) = (Console.Out, Console.Error);
		Console.SetOut(stdout);
		Console.SetError(new StringWriter());
		try
		{
			(await app.RunAsync(["echoes", "-j", "2"])).Should().Be(0);
		}
		finally
		{
			Console.SetOut(oldOut);
			Console.SetError(oldErr);
		}
		var lines = stdout.ToString().Split('\n');
		lines.Should().Contain(l => l.StartsWith("[echo-a] ") && char.IsDigit(l["[echo-a] ".Length]));
		lines.Should().Contain(l => l.StartsWith("[echo-b] ") && char.IsDigit(l["[echo-b] ".Length]));
	}
}

public class ParallelBuild : MakeBuild
{
	public static ConcurrentQueue<string> Log { get; } = new();
	public static CountdownEvent Both { get; set; } = new(2);

	private static void Meet(string name)
	{
		Both.Signal();
		Log.Enqueue(Both.Wait(TimeSpan.FromSeconds(3)) ? $"{name}:met" : $"{name}:alone");
	}

	public Target Left => _ => _.Executes(() => Meet("left"));

	public Target Right => _ => _.Executes(() => Meet("right"));

	public Target Last => _ => _.Executes(() => Log.Enqueue("last"));

	public Command Both2 => _ => _.Named("both").Parallel(Left, Right).Composes(Last);
}

[NotInParallel]
public class ClassParallelTests
{
	[Test]
	public async Task A_parallel_stage_runs_its_targets_side_by_side()
	{
		ParallelBuild.Log.Clear();
		ParallelBuild.Both = new CountdownEvent(2);
		var (oldOut, oldErr) = (Console.Out, Console.Error);
		Console.SetOut(new StringWriter());
		Console.SetError(new StringWriter());
		try
		{
			(await MakeApp.Execute<ParallelBuild>(["both", "-j", "2"])).Should().Be(0);
		}
		finally
		{
			Console.SetOut(oldOut);
			Console.SetError(oldErr);
		}
		ParallelBuild.Log.Take(2).Should().BeEquivalentTo("left:met", "right:met");
		ParallelBuild.Log.Last().Should().Be("last");
	}
}

public record class First;

public record class Second;

public record class SecondsDependency;

public record class InOrder;

public record class Fanned;

public union Ordered(First, Second, SecondsDependency, InOrder, Fanned);

/// <summary>Only a parallel stage runs targets side by side; a list, and everything a list item depends on, stays in order.</summary>
[NotInParallel]
public class OrderTests
{
	private static async Task<List<string>> RunAsync(params string[] args)
	{
		var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
		var app = new MakeApp<Ordered>("build");
		app.Bind(o => o.Value switch
		{
			First => app.Target(async () =>
			{
				log.Enqueue("first:start");
				await Task.Delay(200);
				log.Enqueue("first:end");
			}),
			SecondsDependency => app.Target(() => log.Enqueue("seconds-dependency")),
			Second => app.Target([new SecondsDependency()], () => log.Enqueue("second")),
			InOrder => app.Command([], [new First(), new Second()]),
			Fanned => app.Command().Composes(new First()).Parallel(new Second(), new SecondsDependency()),
			_ => throw new InvalidOperationException()
		});
		var (oldOut, oldErr) = (Console.Out, Console.Error);
		Console.SetOut(new StringWriter());
		Console.SetError(new StringWriter());
		try
		{
			(await app.RunAsync(args)).Should().Be(0);
		}
		finally
		{
			Console.SetOut(oldOut);
			Console.SetError(oldErr);
		}
		return [.. log];
	}

	[Test]
	public async Task A_list_items_dependencies_wait_for_the_item_before_it()
	{
		var log = await RunAsync("in-order", "-j", "4");
		log.Should().Equal("first:start", "first:end", "seconds-dependency", "second");
	}

	[Test]
	public async Task A_parallel_stages_dependencies_wait_for_the_stage_before_it()
	{
		var log = await RunAsync("fanned", "-j", "4");
		log.Take(2).Should().Equal("first:start", "first:end");
	}
}
