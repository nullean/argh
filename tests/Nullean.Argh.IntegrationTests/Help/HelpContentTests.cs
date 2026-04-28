using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class HelpContentTests
{
	private static string TrimLines(string s) =>
		string.Join("\n", s.Split('\n').Select(l => l.TrimEnd()));

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
		var expected = ($"""
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
		TrimLines(text).Should().Be(TrimLines(expected));
	}

	[Fact]
	public void DocLambda_help_matches_expected_text()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"doc-lambda",
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		var expected = ($"""
			Usage: {CliHostPaths.CliHostAssemblyName} doc-lambda --line <string>

			   Documented handler for lambda-style Map (XML appears in help).

			Global options:
			  -h, --help           Show help.
			  --verbose
			  --severity <enum>    Enum default for global-flag parsing regression. [default: Information]
			                       One of: <Trace|Information|Warning>

			Options:
			  -l, --line <string>  [required] Text line to echo.

			Examples:
			  doc-lambda --line hi
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		TrimLines(text).Should().Be(TrimLines(expected));
	}

	[Fact]
	public void BraceDoc_help_shows_braces_from_xml_doc_without_breaking_build()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"brace-doc",
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("Supports {version} and {owner} placeholders.");
	}
}
