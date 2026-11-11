using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class UnionHelpTests
{
	private static string TrimLines(string s) =>
		string.Join("\n", s.Split('\n').Select(l => l.TrimEnd()));

	private static string Help(string command)
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			command,
			"--help");
		result.ExitCode.Should().Be(0);
		return TrimLines(ConsoleOutput.Normalize(CliHostRunner.StdoutText(result)));
	}

	private static string Expected(string s) =>
		TrimLines(s.ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n");

	[Fact]
	public void Flag_mode_help_lists_cases_and_case_flags()
	{
		Help("union-format-flag").Should().Be(Expected($"""
			Usage: {CliHostPaths.CliHostAssemblyName} union-format-flag --format <case>

			   Output in a specific format (flag mode: --format <case>).

			Global options:
			  -h, --help           Show help.
			  -v, --verbose
			  --severity <enum>    Enum default for global-flag parsing regression. [default: information]
			                       One of: <trace|information|warning>
			  -m, --mode <string>  Test-only global mode label (non-bool global short-option coverage). [default: ]

			Options:
			  --format <case>      [required]
			                       One of: <table|json|csv>
			  --table-pretty       Applies when --format is table.
			  --json-pretty        Applies when --format is json.
			  --json-indent <int>  Applies when --format is json. [default: 2]
			"""));
	}

	[Fact]
	public void Argument_mode_help_lists_cases_and_shared_case_flags_once()
	{
		Help("union-format-arg").Should().Be(Expected($"""
			Usage: {CliHostPaths.CliHostAssemblyName} union-format-arg <format>

			   Output in a specific format (argument mode: <format-case> [--pretty] ...).

			Arguments:
			  <format>  One of: <table|json|csv>

			Global options:
			  -h, --help           Show help.
			  -v, --verbose
			  --severity <enum>    Enum default for global-flag parsing regression. [default: information]
			                       One of: <trace|information|warning>
			  -m, --mode <string>  Test-only global mode label (non-bool global short-option coverage). [default: ]

			Options:
			  --pretty             Applies when <format> is table or json.
			  --indent <int>       Applies when <format> is json. [default: 2]
			"""));
	}

	[Fact]
	public void Case_flags_show_plain_defaults_enum_choices_and_negatable_bools()
	{
		var help = Help("union-shape");
		help.Should().Contain(Expected("""
			Options:
			  --shape <case>           [required]
			                           One of: <circle|rect>
			  --circle-radius <float>  Applies when --shape is circle. [default: 1.5]
			  --circle-fill <enum>     Applies when --shape is circle. [default: none]
			                           One of: <solid|dash|none>
			  --rect-width <decimal>   Applies when --shape is rect. [default: 2.5]
			  --[no-]rect-rounded      Applies when --shape is rect. [default: true]
			"""));
	}
}
