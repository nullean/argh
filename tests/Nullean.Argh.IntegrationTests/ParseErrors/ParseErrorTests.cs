using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.ParseErrors;

public class ParseErrorTests
{
	[Fact]
	public void Missing_required_flag_returns_exit_2()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"hello");
		result.ExitCode.Should().Be(2);
	}

	[Fact]
	public void Missing_required_flag_stderr_and_stdout_match_expected_text()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"hello");
		ConsoleOutput.Normalize(CliHostRunner.StderrText(result)).Should().Be(CliHostGoldenOutput.HelloMissingRequiredFlagStderr());
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(result)).Should().Be(CliHostGoldenOutput.HelloMissingRequiredFlagStdout());
	}
}
