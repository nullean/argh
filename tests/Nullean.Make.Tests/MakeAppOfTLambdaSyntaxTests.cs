using FluentAssertions;
using Xunit;
using static Nullean.Make.Shell;

namespace Nullean.Make.Tests.LambdaSyntax;

/// <summary>Delete <c>build/output</c> and run <see cref="System.IO"/> cleanup.</summary>
public record class Wipe;

/// <summary>
/// Compile the solution
/// in Release.
/// </summary>
public record class Compile;

/// <summary>Run the tests.</summary>
public record class Check(string? Filter = null);

/// <summary>Documented, but the explicit description wins.</summary>
public record class Pack;

public record class Undocumented;

/// <summary>Build, check and pack.</summary>
public record class Ship;

public union Lambda(Wipe, Compile, Check, Pack, Undocumented, Ship);

[Collection(nameof(ConsoleCollection))]
public class MakeAppLambdaSyntaxTests
{
	private sealed record Run(int ExitCode, IReadOnlyList<string> Log, string Stdout, string Stderr);

	private static async Task<Run> RunAsync(Action<MakeApp<Lambda>, List<string>>? configure, params string[] args)
	{
		var log = new List<string>();
		var app = new MakeApp<Lambda>("build");
		if (configure is not null)
			configure(app, log);
		else
			app.Bind(t => t.Value switch
			{
				Wipe => app.Target(() => log.Add("wipe")),
				Compile => app.Target([new Wipe()], () => log.Add("compile")),
				Check check => app.Target([new Compile()], () =>
				{
					if (check.Filter is { } f) log.Add($"check:{f}");
					else log.Add("check:all");
				}),
				Pack => app.Target([new Check()], async () =>
				{
					await Task.Yield();
					log.Add("pack");
				}).Description("Pack the packages"),
				Undocumented => app.Target(() => log.Add("undocumented")),
				Ship => app.Command([new Check()], [new Pack()], () => log.Add("ship")),
				_ => throw new InvalidOperationException()
			});

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

	[Fact]
	public async Task Target_with_dependencies_runs_them_first()
	{
		var run = await RunAsync(null, "compile");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("wipe", "compile");
	}

	[Fact]
	public async Task Target_body_can_branch_on_the_case_payload()
	{
		(await RunAsync(null, "check", "--filter", "Unit", "-s")).Log.Should().Equal("check:Unit");
		(await RunAsync(null, "check", "-s")).Log.Should().Equal("check:all");
	}

	[Fact]
	public async Task Async_target_body_runs()
	{
		var run = await RunAsync(null, "pack", "-s");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("pack");
	}

	[Fact]
	public async Task Command_runs_requires_composes_then_its_body()
	{
		var run = await RunAsync(null, "ship");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("wipe", "compile", "check:all", "pack", "ship");
	}

	[Fact]
	public async Task Descriptions_come_from_xml_doc_summaries()
	{
		var run = await RunAsync(null, "--help");
		run.Stdout.Should().Contain("Delete build/output and run IO cleanup.");
		// Multi-line summaries collapse to one line.
		run.Stdout.Should().Contain("Compile the solution in Release.");
		run.Stdout.Should().Contain("Build, check and pack.");
	}

	[Fact]
	public async Task Explicit_description_wins_over_the_doc_summary()
	{
		var run = await RunAsync(null, "--help");
		run.Stdout.Should().Contain("Pack the packages");
		run.Stdout.Should().NotContain("Documented, but the explicit description wins.");
	}

	[Fact]
	public async Task Exec_runs_a_command_line()
	{
		var run = await RunAsync((app, log) => app.Bind(t => t.Value switch
		{
			Wipe => app.Target(() => { Exec("dotnet --version"); log.Add("ran"); }),
			_ => app.Target(() => { })
		}), "wipe");
		run.ExitCode.Should().Be(0, run.Stderr);
		run.Log.Should().Equal("ran");
	}

	[Fact]
	public async Task Exec_failure_fails_the_target()
	{
		var run = await RunAsync((app, log) => app.Bind(t => t.Value switch
		{
			Wipe => app.Target(() => { Exec("dotnet", "definitely-not-a-dotnet-command"); log.Add("after"); }),
			_ => app.Target(() => { })
		}), "wipe");
		run.ExitCode.Should().Be(1);
		run.Stderr.Should().Contain("[make] Target 'wipe' failed");
		run.Log.Should().BeEmpty();
	}
}

public class ShellSplitTests
{
	[Theory]
	[InlineData("dotnet build -c Release", new[] { "dotnet", "build", "-c", "Release" })]
	[InlineData("  dotnet   clean  ", new[] { "dotnet", "clean" })]
	[InlineData("dotnet test \"My Project.csproj\"", new[] { "dotnet", "test", "My Project.csproj" })]
	[InlineData("git commit -m \"say \\\"hi\\\"\"", new[] { "git", "commit", "-m", "say \"hi\"" })]
	[InlineData("tool \"\" last", new[] { "tool", "", "last" })]
	[InlineData("a--b=\"c d\"", new[] { "a--b=c d" })]
	[InlineData("", new string[0])]
	public void Split_follows_quotes_and_escapes(string commandLine, string[] expected) =>
		Shell.Split(commandLine).Should().Equal(expected);

	[Fact]
	public void Unclosed_quote_is_an_error() =>
		FluentActions.Invoking(() => Shell.Split("echo \"oops")).Should().Throw<ArgumentException>();

	[Fact]
	public void Empty_command_line_cannot_run() =>
		FluentActions.Invoking(() => Exec("   ")).Should().Throw<ArgumentException>();
}
