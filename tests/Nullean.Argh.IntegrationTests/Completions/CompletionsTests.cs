using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Completions;

public class CompletionsTests
{
	[Fact]
	public void Zsh_nonempty()
	{
		var result = CliHostRunner.Run("--completions", "zsh");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void Fish_nonempty()
	{
		var result = CliHostRunner.Run("--completions", "fish");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void Zsh_contains_complete_keyword()
	{
		var result = CliHostRunner.Run("--completions", "zsh");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("complete");
	}

	[Fact]
	public void Fish_contains_complete_keyword()
	{
		var result = CliHostRunner.Run("--completions", "fish");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("complete");
	}
}
