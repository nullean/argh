using AwesomeAssertions;

namespace Nullean.Make.Tests.CaseTable;

/// <summary>Deploy to an environment.</summary>
public record class Deploy(string Environment, int Replicas = 1);

public record class Smoke;

public record class Purge;

public union Ops(Purge);

public union Infra(Deploy, Smoke, Ops);

public record class Orphan;

/// <summary>Only ever created through a type parameter, so the generator emits no case table for it.</summary>
public union Unregistered(Orphan);

/// <summary>
/// Cases come from the case table that the Nullean.Make source generator emits at build time,
/// so a case without a parameterless constructor is still a target.
/// </summary>
[NotInParallel]
public class UnionCaseTableTests
{
	private sealed record Run(int ExitCode, IReadOnlyList<string> Log, string Stdout, string Stderr);

	private static async Task<Run> RunAsync(params string[] args)
	{
		var log = new List<string>();
		// Target-typed new: the generator finds this creation through its type, not its syntax.
		MakeApp<Infra> app = new("infra");
		app.Bind(
			i =>
				i.Value switch
				{
					Deploy d => app.Target(() => log.Add($"deploy:{d.Environment}:{d.Replicas}")),
					Smoke => app.Target([new Deploy("staging")], () => log.Add("smoke")),
					Ops { Value: Purge } => app.Target(() => log.Add("purge")),
					_ => throw new InvalidOperationException($"unmapped case {i.Value}")
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
	public async Task Case_with_a_required_parameter_is_a_target()
	{
		var run = await RunAsync("deploy", "--environment", "prod", "--replicas", "3");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("deploy:prod:3");
	}

	[Test]
	public async Task Dependency_on_a_case_with_a_required_parameter_resolves()
	{
		var run = await RunAsync("smoke");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().HaveCount(2);
		run.Log[1].Should().Be("smoke");
	}

	[Test]
	public async Task Nested_union_case_routes_under_its_namespace()
	{
		var run = await RunAsync("ops", "purge");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("purge");
	}

	[Test]
	public async Task Help_lists_every_case()
	{
		var run = await RunAsync("--help");
		run.Stdout.Should().Contain("deploy").And.Contain("Deploy to an environment.").And.Contain("smoke").And.Contain("ops");
	}

	private static MakeApp<T> Create<T>() where T : struct => new("generic");

	[Test]
	public async Task Union_without_a_generated_case_table_explains_why()
	{
		var app = Create<Unregistered>();
		app.Bind(_ => app.Target(() =>
		{ }));
		var stderr = new StringWriter();
		var oldErr = Console.Error;
		Console.SetError(stderr);
		try
		{
			(await app.RunAsync(["orphan"])).Should().Be(1);
		}
		finally
		{
			Console.SetError(oldErr);
		}
		stderr.ToString().Should().Contain("No case table was generated for Unregistered");
	}
}
