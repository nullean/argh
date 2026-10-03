using FluentAssertions;
using Nullean.Argh.Runtime;
using Nullean.Argh.Tests.Fixtures;
using Nullean.Argh.Validation;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Validation;

[Collection("Console")]
public class ArghValidationTests : IDisposable
{
	public ArghValidationTests() => ArghValidation.Clear();

	public void Dispose() => ArghValidation.Clear();

	private static async Task<(int Code, string Err)> Run(params string[] args)
	{
		var err = new StringWriter();
		var previous = Console.Error;
		Console.SetError(err);
		try
		{
			return (await ArghRuntime.RunAsync(args), err.ToString());
		}
		finally
		{
			Console.SetError(previous);
		}
	}

	[Fact]
	public async Task A_registered_validator_failure_prints_like_a_built_in_check_and_exits_2()
	{
		ArghValidation.Register<AsParamsWithCtArgs>(a => a.Port > 1024 ? [] : [new ArghValidationError("must be above 1024", nameof(AsParamsWithCtArgs.Port))]);

		var (code, err) = await Run("as-params-with-ct", "--run-env", "prod", "--run-port", "80");

		code.Should().Be(2);
		err.Should().Contain("Error: --run-port: must be above 1024");
		err.Should().Contain("for usage.");
	}

	[Fact]
	public async Task A_registered_validator_that_passes_lets_the_command_run()
	{
		ArghValidation.Register<AsParamsWithCtArgs>(a => a.Port > 1024 ? [] : [new ArghValidationError("must be above 1024", nameof(AsParamsWithCtArgs.Port))]);

		var (code, _) = await Run("as-params-with-ct", "--run-env", "prod", "--run-port", "8080");

		code.Should().Be(0);
	}

	[Fact]
	public async Task Without_registered_validators_nothing_changes()
	{
		var (code, _) = await Run("as-params-with-ct", "--run-env", "prod", "--run-port", "80");

		code.Should().Be(0);
	}

	[Fact]
	public void TryParseArgh_runs_the_validator_too_and_reports_failure_as_false()
	{
		ArghValidation.Register<AsParamsWithCtArgs>(a => a.Port > 1024 ? [] : [new ArghValidationError("must be above 1024", nameof(AsParamsWithCtArgs.Port))]);
		var err = new StringWriter();
		var previous = Console.Error;
		Console.SetError(err);
		try
		{
			AsParamsWithCtArgs.TryParseArgh(["--run-env", "prod", "--run-port", "80"], out var value).Should().BeFalse();
			value.Should().BeNull();
		}
		finally
		{
			Console.SetError(previous);
		}

		err.ToString().Should().Contain("Error: --run-port: must be above 1024");
	}

	[Fact]
	public async Task A_validator_for_the_global_options_type_runs_before_any_command()
	{
		ArghValidation.Register<TestGlobalCliOptions>(o => o.Verbose ? [new ArghValidationError("verbose is not allowed here", nameof(TestGlobalCliOptions.Verbose))] : []);

		var (code, err) = await Run("--verbose", "hello", "--name", "t");

		code.Should().Be(2);
		err.Should().Contain("Error: --verbose: verbose is not allowed here");
	}
}
