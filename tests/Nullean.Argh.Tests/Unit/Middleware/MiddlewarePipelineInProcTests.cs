using FluentAssertions;
using Nullean.Argh.Runtime;
using Nullean.Argh.Tests.Fixtures;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Middleware;

[Collection("Console")]
public class MiddlewarePipelineInProcTests
{
	[Fact]
	public async Task RunAsync_global_and_per_command_middleware_run()
	{
		TestsGlobalMiddleware.InvokeCount = 0;
		TestsPerCommandMiddleware.InvokeCount = 0;
		var code = await ArghRuntime.RunAsync(["hello", "--name", "t"]);
		code.Should().Be(0);
		TestsGlobalMiddleware.InvokeCount.Should().Be(1);
		TestsPerCommandMiddleware.InvokeCount.Should().Be(1);
	}

	[Fact]
	public async Task RunAsync_middleware_skipped_for_root_help()
	{
		TestsGlobalMiddleware.InvokeCount = 0;
		TestsPerCommandMiddleware.InvokeCount = 0;
		var code = await ArghRuntime.RunAsync(["--help"]);
		code.Should().Be(0);
		TestsGlobalMiddleware.InvokeCount.Should().Be(0);
		TestsPerCommandMiddleware.InvokeCount.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_middleware_skipped_for_version()
	{
		TestsGlobalMiddleware.InvokeCount = 0;
		TestsPerCommandMiddleware.InvokeCount = 0;
		var code = await ArghRuntime.RunAsync(["--version"]);
		code.Should().Be(0);
		TestsGlobalMiddleware.InvokeCount.Should().Be(0);
		TestsPerCommandMiddleware.InvokeCount.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_middleware_skipped_for_completions()
	{
		TestsGlobalMiddleware.InvokeCount = 0;
		TestsPerCommandMiddleware.InvokeCount = 0;
		var code = await ArghRuntime.RunAsync(["--completions", "bash"]);
		code.Should().Be(0);
		TestsGlobalMiddleware.InvokeCount.Should().Be(0);
		TestsPerCommandMiddleware.InvokeCount.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_middleware_skipped_for_command_help()
	{
		TestsGlobalMiddleware.InvokeCount = 0;
		TestsPerCommandMiddleware.InvokeCount = 0;
		var code = await ArghRuntime.RunAsync(["hello", "--help"]);
		code.Should().Be(0);
		TestsGlobalMiddleware.InvokeCount.Should().Be(0);
		TestsPerCommandMiddleware.InvokeCount.Should().Be(0);
	}
}
