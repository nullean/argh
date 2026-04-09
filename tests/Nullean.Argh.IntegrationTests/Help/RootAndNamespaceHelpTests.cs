using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class RootAndNamespaceHelpTests
{
	[Fact]
	public void RootHelp_does_not_list_nested_blob_command()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Be(CliHostGoldenOutput.RootHelpNoColor());
		text.Should().NotContain("blob");
	}

	[Fact]
	public void StorageHelp_lists_blob_namespace_and_not_flat_hello()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"storage",
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Be(CliHostGoldenOutput.StorageHelpNoColor());
		text.Should().NotContain("hello");
	}
}
