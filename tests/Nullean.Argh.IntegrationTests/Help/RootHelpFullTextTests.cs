using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

/// <summary>Asserts stable root help shape (NO_COLOR, normalized); assembly name is fixed for CliHost.</summary>
public class RootHelpFullTextTests
{
	[Fact]
	public void Root_help_full_text_matches_expected_shape()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		// Multiline snapshot: first lines are usage + command listing; exact body may evolve—anchor on required sections.
		text.Should().StartWith($"Usage: {CliHostPaths.CliHostAssemblyName}");
		text.Should().Contain("Commands:");
		text.Should().Contain("hello");
		text.Should().Contain("storage");
		text.Should().Contain("Global options:");
		text.Should().Contain("--verbose");
		text.Should().Contain("--help, -h");
	}
}
