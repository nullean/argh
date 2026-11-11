using FluentAssertions;
using Xunit;

namespace Nullean.Make.Tests;

public class NamespaceHelpBuild : MakeBuild
{
	public Target Clean => _ => _.Description("Delete artifacts").Executes(() => { });

	public class Schema : Namespace
	{
		public Target Update => _ => _.Description("Regenerate the schema").Executes(() => { });
		public Target Validate => _ => _.Description("Fail if the schema is out of date").Executes(() => { });
		public Target Internal => _ => _.Hidden().Executes(() => { });
	}
}

/// <summary>
/// <see cref="MakeApp"/> writes to <see cref="Console"/>, so these tests swap the process-wide writers and
/// must not run in parallel with other tests that do the same.
/// </summary>
[Collection(nameof(ConsoleCollection))]
public class MakeAppNamespaceHelpTests
{
	private static async Task<(int ExitCode, string Stdout)> RunAsync(params string[] args)
	{
		var stdout = new StringWriter();
		var oldOut = Console.Out;
		Console.SetOut(stdout);
		try
		{
			var exit = await MakeApp.Execute<NamespaceHelpBuild>(args);
			return (exit, stdout.ToString());
		}
		finally
		{
			Console.SetOut(oldOut);
		}
	}

	[Fact]
	public async Task Namespace_help_lists_the_namespace_targets()
	{
		var (exit, stdout) = await RunAsync("schema", "--help");
		exit.Should().Be(0);
		stdout.Should().Contain("update").And.Contain("Regenerate the schema");
		stdout.Should().Contain("validate").And.Contain("Fail if the schema is out of date");
		stdout.Should().NotContain("internal");
		// Root help would list the root target.
		stdout.Should().NotContain("Delete artifacts");
	}

	[Fact]
	public async Task Root_help_still_lists_root_targets_and_namespaces()
	{
		var (exit, stdout) = await RunAsync("--help");
		exit.Should().Be(0);
		stdout.Should().Contain("Delete artifacts").And.Contain("Namespaces:");
	}
}
