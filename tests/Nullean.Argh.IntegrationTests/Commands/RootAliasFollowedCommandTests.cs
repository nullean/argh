using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Commands;

/// <summary>Subprocess tests for a namespace that registers <c>MapAndRootAlias&lt;T&gt;</c> followed by additional <c>Map&lt;T&gt;</c> commands.</summary>
public class RootAliasFollowedCommandTests
{
	[Fact]
	public void No_args_in_namespace_executes_alias_target()
	{
		var result = CliHostRunner.Run("alias-followed");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:alias-followed-build");
	}

	[Fact]
	public void First_followup_command_is_accessible()
	{
		var result = CliHostRunner.Run("alias-followed", "diff");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:alias-followed-diff");
	}

	[Fact]
	public void Second_followup_command_is_accessible()
	{
		var result = CliHostRunner.Run("alias-followed", "serve");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("marker:alias-followed-serve");
	}
}
