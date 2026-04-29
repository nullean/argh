using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class EnumHelpTests
{
	private static string TrimLines(string s) =>
		string.Join("\n", s.Split('\n').Select(l => l.TrimEnd()));

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
			Usage: {CliHostPaths.CliHostAssemblyName} enum-cmd --color <enum> --name <string>

			   Enum and short options.

			Global options:
			  -h, --help                    Show help.
			  -v, --verbose
			  --severity <enum>             Enum default for global-flag parsing regression. [default: information]
			                                One of: <trace|information|warning>

			Options:
			  -c, --colour, --color <enum>  [required] Pick a color.
			                                One of: <red|blue>
			  -n, --name <string>           [required] Display name
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		TrimLines(text).Should().Be(TrimLines(expected));
	}

	[Fact]
	public void ReadOnlySet_of_enum_lists_allowed_members_in_help()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"color-set",
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("Combination of: <red|blue>");
	}
}
