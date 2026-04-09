using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

/// <summary>Asserts stable root help (NO_COLOR, normalized); assembly name via <see cref="CliHostPaths.CliHostAssemblyName"/>.</summary>
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
		text.Should().Be(CliHostGoldenOutput.RootHelpNoColor());
	}
}
