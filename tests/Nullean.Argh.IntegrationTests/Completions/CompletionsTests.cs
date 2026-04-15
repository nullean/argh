using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Completions;

public class CompletionsTests
{
	[Fact]
	public void Zsh_nonempty()
	{
		var result = CliHostRunner.Run("__completion", "zsh");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void Fish_nonempty()
	{
		var result = CliHostRunner.Run("__completion", "fish");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void Zsh_contains_complete_keyword()
	{
		var result = CliHostRunner.Run("__completion", "zsh");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("complete");
	}

	[Fact]
	public void Fish_contains_complete_keyword()
	{
		var result = CliHostRunner.Run("__completion", "fish");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("complete");
	}

	[Fact]
	public void Complete_bash_root_emits_command_names()
	{
		var result = CliHostRunner.Run("__complete", "bash", "--");
		result.ExitCode.Should().Be(0);
		var stdout = CliHostRunner.StdoutText(result);
		stdout.Should().Contain("hello");
		stdout.Should().Contain("storage");
	}

	[Fact]
	public void Complete_bash_after_storage_namespace_emits_subcommands()
	{
		var result = CliHostRunner.Run("__complete", "bash", "--", "storage", "");
		result.ExitCode.Should().Be(0);
		var stdout = CliHostRunner.StdoutText(result);
		stdout.Should().Contain("blob");
		stdout.Should().Contain("list");
	}

	[Fact]
	public void Complete_unknown_shell_exits_nonzero()
	{
		var result = CliHostRunner.Run("__complete", "pwsh", "--");
		result.ExitCode.Should().Be(2);
	}
}
