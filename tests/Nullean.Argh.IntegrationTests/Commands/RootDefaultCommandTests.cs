using AwesomeAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;

namespace Nullean.Argh.IntegrationTests.Commands;

/// <summary>Subprocess tests for app-root <c>MapAndRootAlias</c> (and <c>MapRoot</c> in namespaces) defaults.</summary>
public class RootDefaultCommandTests
{
	[Test]
	public void No_args_invokes_root_default_handler()
	{
		var result = CliHostRunner.Run();
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:root-default");
	}

	[Test]
	public void Global_flags_only_invokes_root_default_handler()
	{
		var result = CliHostRunner.Run("--verbose");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:root-default");
	}

	[Test]
	public void Global_short_verbose_only_invokes_root_default_handler()
	{
		var result = CliHostRunner.Run("-v");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:root-default");
	}

	[Test]
	public void Root_alias_command_flag_before_subcommand_name_routes_to_default()
	{
		var result = CliHostRunner.Run("--prefetch-regression");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:root-default:prefetch");
	}

	[Test]
	public void Namespace_only_with_required_namespace_options_invokes_namespace_root()
	{
		var result = CliHostRunner.Run("storage", "--prefix", "p1");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:storage-ns-root");
	}

	[Test]
	public void Root_help_mentions_root_command_section()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("(default command)");
		text.Should().Contain("Integration-test default when no sub-command is given at the app root.");
	}

	[Test]
	public void Storage_help_mentions_namespace_root_section()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"storage",
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("(default command)");
		text.Should().Contain("Integration-test default when only the storage namespace is selected.");
		text.Should().Contain("Namespace default remarks for help layout tests.");
	}
}
