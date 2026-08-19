using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Binding;

public class UnionBindingTests
{
	// ── Flag mode ──────────────────────────────────────────────────────────────

	[Fact]
	public void Flag_mode_json_with_pretty()
	{
		var result = CliHostRunner.Run("union-format-flag", "--format", "json", "--json-pretty");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("json:pretty=True:indent=2");
	}

	[Fact]
	public void Flag_mode_json_with_indent()
	{
		var result = CliHostRunner.Run("union-format-flag", "--format", "json", "--json-indent", "4");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("json:pretty=False:indent=4");
	}

	[Fact]
	public void Flag_mode_table_with_pretty()
	{
		var result = CliHostRunner.Run("union-format-flag", "--format", "table", "--table-pretty");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("table:pretty=True");
	}

	[Fact]
	public void Flag_mode_csv_no_props()
	{
		var result = CliHostRunner.Run("union-format-flag", "--format", "csv");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("csv");
	}

	[Fact]
	public void Flag_mode_silently_ignores_unused_case_prop_flags()
	{
		// --json-pretty is for json; when --format is table it's silently unused (not an error)
		var result = CliHostRunner.Run("union-format-flag", "--format", "table", "--json-pretty");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("table:pretty=False");
	}

	[Fact]
	public void Flag_mode_both_table_and_json_have_pretty_flag_no_collision()
	{
		// table has --table-pretty; json has --json-pretty; no collision
		var tableResult = CliHostRunner.Run("union-format-flag", "--format", "table", "--table-pretty");
		tableResult.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(tableResult).Trim().Should().Be("table:pretty=True");

		var jsonResult = CliHostRunner.Run("union-format-flag", "--format", "json", "--json-pretty");
		jsonResult.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(jsonResult).Trim().Should().Be("json:pretty=True:indent=2");
	}

	[Fact]
	public void Flag_mode_invalid_format_exits_nonzero()
	{
		var result = CliHostRunner.Run("union-format-flag", "--format", "xml");
		result.ExitCode.Should().Be(2);
	}

	// ── Argument mode ──────────────────────────────────────────────────────────

	[Fact]
	public void Argument_mode_json_with_pretty()
	{
		var result = CliHostRunner.Run("union-format-arg", "json", "--pretty");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("json:pretty=True:indent=2");
	}

	[Fact]
	public void Argument_mode_json_with_indent()
	{
		var result = CliHostRunner.Run("union-format-arg", "json", "--indent", "4");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("json:pretty=False:indent=4");
	}

	[Fact]
	public void Argument_mode_table_with_pretty()
	{
		var result = CliHostRunner.Run("union-format-arg", "table", "--pretty");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("table:pretty=True");
	}

	[Fact]
	public void Argument_mode_csv_no_props()
	{
		var result = CliHostRunner.Run("union-format-arg", "csv");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("csv");
	}

	[Fact]
	public void Argument_mode_invalid_case_exits_nonzero()
	{
		var result = CliHostRunner.Run("union-format-arg", "xml");
		result.ExitCode.Should().Be(2);
	}
}
