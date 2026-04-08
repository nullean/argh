using FluentAssertions;
using Nullean.Argh;
using Xunit;

namespace Nullean.Argh.Tests;

[Collection("Console")]
public class ArghCliTests
{
	[Fact]
	public async Task RunWithCaptureAsync_func_captures_stdout_stderr_and_exit_code()
	{
		var result = await ArghCli.RunWithCaptureAsync(() =>
		{
			Console.WriteLine("line-out");
			Console.Error.WriteLine("line-err");
			return Task.FromResult(0);
		});

		result.ExitCode.Should().Be(0);
		result.Stdout.Should().Contain("line-out");
		result.Stderr.Should().Contain("line-err");
	}

	[Fact]
	public async Task RunWithCaptureAsync_runner_receives_args_and_captures_output()
	{
		var result = await ArghCli.RunWithCaptureAsync(
			new[] { "a", "b" },
			args =>
			{
				Console.WriteLine(string.Join(",", args));
				return Task.FromResult(42);
			});

		result.ExitCode.Should().Be(42);
		result.Stdout.Trim().Should().Be("a,b");
		result.Stderr.Should().BeEmpty();
	}

	[Fact]
	public async Task RunWithCaptureAsync_commandLine_splits_quotes_minimally()
	{
		var result = await ArghCli.RunWithCaptureAsync(
			"one  \"two three\"  four",
			args =>
			{
				Console.WriteLine(string.Join("|", args));
				return Task.FromResult(0);
			});

		result.ExitCode.Should().Be(0);
		result.Stdout.Trim().Should().Be("one|two three|four");
	}
}
