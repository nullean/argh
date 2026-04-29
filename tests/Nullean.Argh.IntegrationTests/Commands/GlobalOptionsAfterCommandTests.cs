using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Commands;

/// <summary>
/// Global options must parse in the command runner for tokens that appear after the route
/// (subcommand name). Leading prefetch only peels globals before the first non-option token;
/// shorts and longs after the command rely on <c>OptionsInjected</c> + generated <c>TryApplyShortFlag</c>.
/// </summary>
public class GlobalOptionsAfterCommandTests
{
	[Fact]
	public void NinHello_NoOptionsInjection_globals_parse_after_route()
	{
		var result = CliHostRunner.Run("nin-hello", "--name", "who", "-m", "trail");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Contain("nin:who");
	}

	[Fact]
	public void Hello_global_bool_short_after_command_flags()
	{
		var result = CliHostRunner.Run("hello", "--name", "x", "-v");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("ok:x");
	}

	[Fact]
	public void Hello_global_mode_short_after_command_flags()
	{
		var result = CliHostRunner.Run("hello", "--name", "x", "-m", "alpha");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("ok:x:alpha");
	}

	[Fact]
	public void Hello_global_mode_short_equals_after_command_flags()
	{
		var result = CliHostRunner.Run("hello", "--name", "x", "-m=beta");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("ok:x:beta");
	}

	[Fact]
	public void Hello_global_mode_long_after_command_flags()
	{
		var result = CliHostRunner.Run("hello", "--name", "x", "--mode", "gamma");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("ok:x:gamma");
	}

	[Fact]
	public void Hello_global_enum_long_after_command_flags()
	{
		var result = CliHostRunner.Run("hello", "--name", "x", "--severity", "warning");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("ok:x");
	}

	[Fact]
	public void Storage_list_global_short_after_namespace_flag()
	{
		var result = CliHostRunner.Run("storage", "list", "--prefix", "p", "-m", "ns");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("storage-list:ns");
	}
}
