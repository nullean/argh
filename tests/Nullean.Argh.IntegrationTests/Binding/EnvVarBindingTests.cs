using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Binding;

public class EnvVarBindingTests
{
	[Fact]
	public void Env_attribute_flag_takes_value_from_cli_when_provided()
	{
		var result = CliHostRunner.Run("env-cmd", "--token", "cli-token");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-token:cli-token");
	}

	[Fact]
	public void Env_attribute_flag_falls_back_to_env_var_when_cli_flag_absent()
	{
		var env = new Dictionary<string, string> { ["MY_APP_TOKEN"] = "env-token-value" };
		var result = CliHostRunner.Run(env, "env-cmd");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-token:env-token-value");
	}

	[Fact]
	public void Env_attribute_flag_is_null_when_neither_cli_nor_env_provided()
	{
		var result = CliHostRunner.Run("env-cmd");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-token:");
	}

	[Fact]
	public void Env_attribute_cli_flag_takes_precedence_over_env_var()
	{
		var env = new Dictionary<string, string> { ["MY_APP_TOKEN"] = "env-value" };
		var result = CliHostRunner.Run(env, "env-cmd", "--token", "cli-wins");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-token:cli-wins");
	}

	[Fact]
	public void Env_attribute_required_flag_satisfied_by_env_var()
	{
		var env = new Dictionary<string, string> { ["MY_APP_SECRET"] = "super-secret" };
		var result = CliHostRunner.Run(env, "env-required");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-secret:super-secret");
	}

	[Fact]
	public void Env_attribute_required_flag_fails_when_neither_cli_nor_env_provided()
	{
		var result = CliHostRunner.Run("env-required");
		result.ExitCode.Should().NotBe(0);
		CliHostRunner.StderrText(result).Should().Contain("--secret");
	}

	[Fact]
	public void Env_attribute_bool_flag_set_by_env_var_true()
	{
		var env = new Dictionary<string, string> { ["MY_APP_VERBOSE"] = "true" };
		var result = CliHostRunner.Run(env, "env-bool");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-verbose:True");
	}

	[Fact]
	public void Env_attribute_bool_flag_env_var_0_means_false()
	{
		var env = new Dictionary<string, string> { ["MY_APP_VERBOSE"] = "0" };
		var result = CliHostRunner.Run(env, "env-bool");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-verbose:False");
	}

	[Fact]
	public void Env_attribute_bool_flag_cli_wins_over_env_var()
	{
		var env = new Dictionary<string, string> { ["MY_APP_VERBOSE"] = "false" };
		var result = CliHostRunner.Run(env, "env-bool", "--verbose");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-verbose:True");
	}

	[Fact]
	public void Env_attribute_shown_in_help_output()
	{
		var result = CliHostRunner.Run("env-cmd", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("[env: MY_APP_TOKEN]");
	}

	[Fact]
	public void Env_on_global_option_falls_back_to_env_var()
	{
		var env = new Dictionary<string, string> { ["TEST_API_URL"] = "https://api.example.com" };
		var result = CliHostRunner.Run(env, "env-global");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-global-api-url:https://api.example.com");
	}

	[Fact]
	public void Env_on_global_option_cli_flag_wins_over_env_var()
	{
		var env = new Dictionary<string, string> { ["TEST_API_URL"] = "https://from-env.example.com" };
		var result = CliHostRunner.Run(env, "env-global", "--api-url", "https://from-cli.example.com");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-global-api-url:https://from-cli.example.com");
	}

	[Fact]
	public void Env_on_global_option_null_when_neither_cli_nor_env()
	{
		var result = CliHostRunner.Run("env-global");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-global-api-url:");
	}

	[Fact]
	public void Env_on_namespace_option_falls_back_to_env_var()
	{
		var env = new Dictionary<string, string> { ["TEST_NS_KEY"] = "from-env-key" };
		var result = CliHostRunner.Run(env, "env-ns", "key");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-ns-key:from-env-key");
	}

	[Fact]
	public void Env_on_namespace_option_cli_flag_wins_over_env_var()
	{
		var env = new Dictionary<string, string> { ["TEST_NS_KEY"] = "from-env" };
		var result = CliHostRunner.Run(env, "env-ns", "key", "--key", "from-cli");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("env-ns-key:from-cli");
	}
}
