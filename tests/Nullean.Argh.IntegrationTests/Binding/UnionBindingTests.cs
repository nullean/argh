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
		CliHostRunner.StderrText(result).Should().Contain("Error: invalid value for --format: 'xml'. Expected one of: table, json, csv.");
	}

	[Fact]
	public void Flag_mode_missing_selector_reports_missing_flag()
	{
		var result = CliHostRunner.Run("union-format-flag", "--json-pretty");
		result.ExitCode.Should().Be(2);
		CliHostRunner.StderrText(result).Should().Contain("Error: missing required flag --format.");
	}

	[Fact]
	public void Flag_mode_unparseable_case_flag_value_exits_nonzero()
	{
		var result = CliHostRunner.Run("union-format-flag", "--format", "json", "--json-indent", "abc");
		result.ExitCode.Should().Be(2);
		var stderr = CliHostRunner.StderrText(result);
		stderr.Should().Contain("Error: invalid value for --json-indent: 'abc'.");
		stderr.Should().Contain("--json-indent <int>");
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
		CliHostRunner.StderrText(result).Should().Contain("Error: invalid value for <format>: 'xml'. Expected one of: table, json, csv.");
	}

	[Fact]
	public void Argument_mode_unparseable_case_flag_value_exits_nonzero()
	{
		var result = CliHostRunner.Run("union-format-arg", "json", "--indent", "x");
		result.ExitCode.Should().Be(2);
		CliHostRunner.StderrText(result).Should().Contain("Error: invalid value for --indent: 'x'.");
	}
}

public class UnionCasePropertyTypeTests
{
	private static string Out(params string[] args)
	{
		var result = CliHostRunner.Run(args);
		result.ExitCode.Should().Be(0, CliHostRunner.StderrText(result));
		return CliHostRunner.StdoutText(result).Trim();
	}

	[Fact]
	public void Defaults_bind_when_no_case_flags_are_given() =>
		// Fill.None = 5: a non-zero enum default must bind by name, not by its underlying value.
		Out("union-shape", "--shape", "circle").Should().Be("circle:radius=1.5:fill=None");

	[Fact]
	public void Float_and_enum_case_flags_bind() =>
		Out("union-shape", "--shape", "circle", "--circle-radius", "2.25", "--circle-fill", "dash")
			.Should().Be("circle:radius=2.25:fill=Dashed");

	[Fact]
	public void Decimal_case_flag_binds() =>
		Out("union-shape", "--shape", "rect", "--rect-width", "3.75").Should().Be("rect:width=3.75:rounded=True");

	[Fact]
	public void Bool_that_defaults_to_true_switches_off_with_no_prefix() =>
		Out("union-shape", "--shape", "rect", "--no-rect-rounded").Should().Be("rect:width=2.5:rounded=False");

	[Fact]
	public void Unknown_enum_value_exits_2()
	{
		var result = CliHostRunner.Run("union-shape", "--shape", "circle", "--circle-fill", "bogus");
		result.ExitCode.Should().Be(2);
		CliHostRunner.StderrText(result).Should().Contain("Error: invalid value for --circle-fill: 'bogus'.");
	}

	[Fact]
	public void Unparseable_decimal_exits_2()
	{
		var result = CliHostRunner.Run("union-shape", "--shape", "rect", "--rect-width", "x");
		result.ExitCode.Should().Be(2);
		CliHostRunner.StderrText(result).Should().Contain("Error: invalid value for --rect-width: 'x'.");
	}
}
