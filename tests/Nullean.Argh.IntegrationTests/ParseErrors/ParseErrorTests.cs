using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.ParseErrors;

public class ParseErrorTests
{
	private static string TrimLines(string s) =>
		string.Join("\n", s.Split('\n').Select(l => l.TrimEnd()));

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
		ConsoleOutput.Normalize(CliHostRunner.StderrText(result)).Should().Be("Error: missing required flag --name.\n");
		var expectedOut = ($"""
			Usage: {CliHostPaths.CliHostAssemblyName} hello --name <string>

			   Greet someone by name.

			Global options:
			  -h, --help         Show help.
			  --verbose
			  --severity <enum>  Enum default for global-flag parsing regression. [default: Information]
			                     One of: <Trace|Information|Warning>

			Options:
			  --name <string>    [required] The name to greet.

			Notes:  See DocLambdaEcho; set name.

			Examples:
			  hello --name world
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		TrimLines(ConsoleOutput.Normalize(CliHostRunner.StdoutText(result))).Should().Be(TrimLines(expectedOut));
	}
}
