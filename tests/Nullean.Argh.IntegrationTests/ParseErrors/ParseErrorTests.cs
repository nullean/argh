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

	[Fact]
	public void Command_flag_typo_returns_exit_2()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"hello", "--nme", "world");
		result.ExitCode.Should().Be(2);
	}

	[Fact]
	public void Command_flag_typo_suggests_did_you_mean_and_prints_flag_help()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"hello", "--nme", "world");
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: unknown option '--nme'. Did you mean '--name'?");
		err.Should().Contain("--name <string>");
		err.Should().Contain($"Run '{CliHostPaths.CliHostAssemblyName} hello --help' for usage.");
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(result)).Should().BeEmpty();
	}

	[Fact]
	public void Command_flag_equals_syntax_typo_returns_exit_2()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"hello", "--nme=world");
		result.ExitCode.Should().Be(2);
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: unknown option '--nme'. Did you mean '--name'?");
	}

	[Fact]
	public void Command_completely_unknown_flag_returns_exit_2_with_run_hint()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"hello", "--zzz", "x");
		result.ExitCode.Should().Be(2);
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: unknown option '--zzz'.");
		err.Should().Contain($"Run '{CliHostPaths.CliHostAssemblyName} hello --help' for usage.");
	}

	[Fact]
	public void Command_unknown_short_flag_returns_exit_2_with_run_hint()
	{
		// enum-cmd has -n and -c; passing -z is unknown
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"enum-cmd", "-z", "val");
		result.ExitCode.Should().Be(2);
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: unknown short option '-z'.");
		err.Should().Contain($"Run '{CliHostPaths.CliHostAssemblyName} enum-cmd --help' for usage.");
	}

	[Fact]
	public void Unknown_namespace_command_no_match_prints_ns_help_hint()
	{
		// 'storage' namespace has 'list' and 'blob' — 'zzz' has no close match
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"storage", "zzz");
		result.ExitCode.Should().Be(2);
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: unknown command or namespace 'zzz'.");
		err.Should().Contain($"Run '{CliHostPaths.CliHostAssemblyName} storage --help' for usage.");
	}

	[Fact]
	public void Unknown_root_command_no_match_prints_root_help_hint()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"zzz-completely-unknown");
		result.ExitCode.Should().Be(2);
		var err = ConsoleOutput.Normalize(CliHostRunner.StderrText(result));
		err.Should().Contain("Error: unknown command or namespace 'zzz-completely-unknown'.");
		err.Should().Contain($"Run '{CliHostPaths.CliHostAssemblyName} --help' for usage.");
	}

	[Fact]
	public void Non_nullable_global_option_property_with_default_is_not_required()
	{
		// --severity is FixtureSeverity (non-nullable enum) with default FixtureSeverity.Information
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"count-cmd", "--count", "1");
		result.ExitCode.Should().Be(0);
	}

	[Fact]
	public void Non_nullable_AsParameters_property_with_default_is_not_required()
	{
		// MultiEnumAsParamsArgs.Severity is FixtureSeverity (non-nullable) with default FixtureSeverity.Information
		// [AsParameters("mix")] sets a flag prefix only, not a sub-command word
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"multi-enum-as-params");
		result.ExitCode.Should().Be(0);
	}

	[Fact]
	public void Non_nullable_method_param_with_default_is_not_required()
	{
		// ValidateNonNullableRange has int pagePer = 20 (non-nullable with explicit default)
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"validate-non-nullable-range");
		result.ExitCode.Should().Be(0);
	}
}
