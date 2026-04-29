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
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: missing required flag --name.");
		err.Should().Contain("--name <string>");
		err.Should().Contain($"Run '{CliHostPaths.CliHostAssemblyName} hello --help' for usage.");
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(result)).Should().BeEmpty();
	}

	[Fact]
	public void Global_flag_missing_value_prints_flag_help_excerpt()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"--severity");
		result.ExitCode.Should().Be(2);
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: missing value for flag --severity.");
		err.Should().Contain("--severity <enum>");
		err.Should().Contain($"Run '{CliHostPaths.CliHostAssemblyName} --help' for usage.");
	}

	[Fact]
	public void Global_flag_typo_suggests_did_you_mean_and_prints_flag_help()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"--severiy", "trace");
		result.ExitCode.Should().Be(2);
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: unknown option '--severiy'. Did you mean '--severity'?");
		err.Should().Contain("--severity <enum>");
	}
}
