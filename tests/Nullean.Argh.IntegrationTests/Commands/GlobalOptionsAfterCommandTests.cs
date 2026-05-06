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

	/// <summary>
	/// Regression: global enum flag passed after the command name must be re-parsed (not silently dropped).
	/// Previously, enum re-parsing in the per-command reconstruct block was missing; the trailing --severity
	/// was ignored and the static default (Information) was always used.
	/// </summary>
	[Fact]
	public void Severity_cmd_global_enum_after_command_is_applied()
	{
		var result = CliHostRunner.Run("severity-cmd", "--severity", "warning");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("severity:Warning");
	}

	/// <summary>
	/// Regression: global enum flag before the command name must still work (leading prefetch path).
	/// </summary>
	[Fact]
	public void Severity_cmd_global_enum_before_command_is_applied()
	{
		var result = CliHostRunner.Run("--severity", "trace", "severity-cmd");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("severity:Trace");
	}

	/// <summary>
	/// Regression: cross-assembly [AsParameters] DTO with nullable string? properties (no explicit default)
	/// previously generated CS8600 — <c>string options__path = __rt_default.Path</c> where Path is string?.
	/// After the fix the generated local is declared as <c>string?</c>.
	/// </summary>
	[Fact]
	public void AsParams_cross_assembly_nullable_string_properties_compile_without_cs8600()
	{
		// If the generator emits string (non-nullable) for string? cross-assembly properties the
		// project won't build; this test passing proves the generated code compiles cleanly.
		var result = CliHostRunner.Run("as-params-referenced-dto", "--path", "docs");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("as-params-referenced:docs:null:null");
	}

	/// <summary>
	/// Regression: cross-assembly [AsParameters] DTO with nullable enum (Nullable&lt;T&gt;, not NRT)
	/// previously generated CS8600 — <c>IsolatedSource source = __rt_default.Source</c> where Source is
	/// IsolatedSource?. After the fix IsNullableAnnotated also captures Nullable&lt;T&gt; value types.
	/// </summary>
	[Fact]
	public void AsParams_cross_assembly_nullable_enum_property_binds_when_provided()
	{
		var result = CliHostRunner.Run("as-params-referenced-dto", "--path", "docs", "--source", "remote");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("as-params-referenced:docs:null:Remote");
	}

	[Fact]
	public void AsParams_cross_assembly_nullable_enum_property_is_null_when_omitted()
	{
		var result = CliHostRunner.Run("as-params-referenced-dto", "--path", "docs");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("as-params-referenced:docs:null:null");
	}

	[Fact]
	public void Storage_list_global_short_after_namespace_flag()
	{
		var result = CliHostRunner.Run("storage", "list", "--prefix", "p", "-m", "ns");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("storage-list:ns");
	}

	[Fact]
	public void AsParams_referenced_dto_global_short_and_command_short_flags_together()
	{
		var result = CliHostRunner.Run("as-params-referenced-dto", "-v", "-p", "docs");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("as-params-referenced:docs:null:null");
	}
}
