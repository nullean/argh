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
		var expected = ($"""
			Usage: {CliHostPaths.CliHostAssemblyName} hello --name <string>

			   Greet someone by name.

			Global options:
			  --help, -h       Show help.
			  --verbose        

			Options:
			  --name <string>  [required]
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		text.Should().Be(expected);
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

			   Documented handler for lambda-style Add (XML appears in help).

			Global options:
			  --help, -h       Show help.
			  --verbose        

			Options:
			  --line <string>  [required]
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		text.Should().Be(expected);
	}
}
