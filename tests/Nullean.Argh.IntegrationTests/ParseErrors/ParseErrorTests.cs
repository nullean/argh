using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.ParseErrors;

public class ParseErrorTests
{
	[Fact]
	public void Missing_required_flag_returns_exit_2()
	{
		var result = CliHostRunner.Run("hello");
		result.ExitCode.Should().Be(2);
	}

	[Fact]
	public void Missing_required_flag_stderr_has_Error()
	{
		var result = CliHostRunner.Run("hello");
		CliHostRunner.StderrText(result).Should().Contain("Error:");
	}

	[Fact]
	public void Missing_required_flag_stdout_has_Usage()
	{
		var result = CliHostRunner.Run("hello");
		CliHostRunner.StdoutText(result).Should().Contain("Usage:");
	}
}
