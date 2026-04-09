using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class RootAndNamespaceHelpTests
{
	[Fact]
	public void RootHelp_contains_storage_and_hello_not_blob()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("storage");
		text.Should().Contain("hello");
		text.Should().NotContain("blob");
	}

	[Fact]
	public void StorageHelp_contains_list_and_blob_not_hello()
	{
		var result = CliHostRunner.Run("storage", "--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("list");
		text.Should().Contain("blob");
		text.Should().NotContain("hello");
	}
}
