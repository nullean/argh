using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Commands;

/// <summary>Subprocess tests for <see cref="Nullean.Argh.Builder.IArghBuilder.MapAndRootAlias{T}"/> scoped to the <c>alias-scope</c> namespace.</summary>
public class RootAliasCommandTests
{
	[Fact]
	public void No_args_in_namespace_executes_alias_target()
	{
		var result = CliHostRunner.Run("alias-scope");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:alias-build output=null force=null");
	}

	[Fact]
	public void Flags_only_route_to_alias_target()
	{
		var result = CliHostRunner.Run("alias-scope", "--output", ".artifacts/html");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:alias-build output=.artifacts/html force=null");
	}

	[Fact]
	public void Alias_target_is_still_accessible_as_named_command()
	{
		var result = CliHostRunner.Run("alias-scope", "build", "--output", "dist");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:alias-build output=dist force=null");
	}

	[Fact]
	public void Other_command_in_type_is_accessible()
	{
		var result = CliHostRunner.Run("alias-scope", "serve", "--port", "8080");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:alias-serve port=8080");
	}

	[Fact]
	public void Namespace_help_shows_alias_section_not_full_option_list()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"alias-scope", "--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("(default: build)");
		text.Should().Contain("Build the documentation set");
		text.Should().Contain("Alias for");
		text.Should().Contain("alias-scope build");
		text.Should().NotContain("Options for this default:");
		text.Should().NotContain("--output");
	}

	[Fact]
	public void Namespace_help_still_lists_all_commands()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"alias-scope", "--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("build");
		text.Should().Contain("serve");
	}
}
