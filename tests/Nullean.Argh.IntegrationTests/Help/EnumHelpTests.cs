using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class EnumHelpTests
{
	[Fact]
	public void Enum_cmd_help_matches_expected_text()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"enum-cmd",
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		var expected = ($"""
			Usage: {CliHostPaths.CliHostAssemblyName} enum-cmd --color <string> --name <string>

			   Enum and short options.

			Global options:
			  --help, -h        Show help.
			  --verbose         

			Options:
			  --color <string>  [required] [values: Red, Blue]
			  --name <string>   [required]
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		text.Should().Be(expected);
	}
}
