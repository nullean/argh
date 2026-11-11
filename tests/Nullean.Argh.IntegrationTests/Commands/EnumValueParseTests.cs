using AwesomeAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;

namespace Nullean.Argh.IntegrationTests.Commands;

public class EnumValueParseTests
{
	[Test]
	[Arguments("fire-red", "FireRed")]
	[Arguments("FIRE-RED", "FireRed")]
	[Arguments("ocean-blue", "OceanBlue")]
	[Arguments("green", "Green")]
	public void EnumValue_custom_cli_strings_parse_to_correct_member(string cliInput, string expectedMember)
	{
		var result = CliHostRunner.Run(new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" }, "enum-value-cmd", "--palette", cliInput);
		result.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(result)).Should().Contain($"enum-value:{expectedMember}");
	}

	[Test]
	public void EnumValue_old_identifier_name_is_rejected()
	{
		var result = CliHostRunner.Run(new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" }, "enum-value-cmd", "--palette", "firered");
		result.ExitCode.Should().Be(2);
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: invalid value for --palette: 'firered'.");
	}

	[Test]
	public void EnumValue_invalid_value_reports_error_with_flag_name()
	{
		var result = CliHostRunner.Run(new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" }, "enum-value-cmd", "--palette", "purple");
		result.ExitCode.Should().Be(2);
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: invalid value for --palette: 'purple'.");
	}
}
