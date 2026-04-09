using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class HelpContentTests
{
	[Fact]
	public void HelloHelp_with_NO_COLOR_omits_ansi()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"hello",
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().NotContain("\x1b");
	}

	[Fact]
	public void HelloHelp_contains_name_option()
	{
		var result = CliHostRunner.Run("hello", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("--name <string>");
	}

	[Fact]
	public void HelloHelp_contains_usage_and_options()
	{
		var result = CliHostRunner.Run("hello", "--help");
		result.ExitCode.Should().Be(0);
		var o = CliHostRunner.StdoutText(result);
		o.Should().Contain("Usage:");
		o.Should().Contain("Options:");
		o.Should().Contain("--help, -h");
	}

	[Fact]
	public void DocLambda_help_lists_documented_param()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"doc-lambda",
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("doc-lambda");
		text.Should().Contain("--line");
	}
}
