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
	public async Task RunAsync_middleware_sees_bound_AsParameters_objects_with_their_flags()
	{
		var code = await ArghRuntime.RunAsync(["as-params-with-ct", "--run-env", "prod", "--run-port", "8080"]);

		code.Should().Be(0);
		var arg = TestsGlobalMiddleware.LastArguments.Should().Contain(a => a.Value is AsParamsWithCtArgs).Which;
		((AsParamsWithCtArgs)arg.Value).Port.Should().Be(8080);
		arg.MemberFlags.Should().Contain("Port", "run-port");
	}

	[Fact]
	public void ReportError_writes_the_generated_parsers_format_and_sets_exit_code_2()
	{
		var ctx = new Nullean.Argh.Middleware.CommandContext(["x"], []);
		var err = new StringWriter();
		var previous = Console.Error;
		Console.SetError(err);
		try { ctx.ReportError("must be positive", "run-port"); }
		finally { Console.SetError(previous); }

		err.ToString().Should().Be($"Error: --run-port: must be positive{Environment.NewLine}");
		ctx.ExitCode.Should().Be(2);
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
		var code = await ArghRuntime.RunAsync(["__completion", "bash"]);
		code.Should().Be(0);
		TestsGlobalMiddleware.InvokeCount.Should().Be(0);
		TestsPerCommandMiddleware.InvokeCount.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_middleware_skipped_for_complete()
	{
		TestsGlobalMiddleware.InvokeCount = 0;
		TestsPerCommandMiddleware.InvokeCount = 0;
		var code = await ArghRuntime.RunAsync(["__complete", "bash", "--"]);
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
