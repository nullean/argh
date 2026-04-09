using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Middleware;

public class MiddlewareStderrTests
{
	[Fact]
	public void Hello_emits_global_and_per_command_middleware_markers_on_stderr()
	{
		var result = CliHostRunner.Run("hello", "--name", "x");
		result.ExitCode.Should().Be(0);
		var err = CliHostRunner.StderrText(result);
		err.Should().Contain("[tests:middleware:global]");
		err.Should().Contain("[tests:middleware:per-command]");
	}
}
