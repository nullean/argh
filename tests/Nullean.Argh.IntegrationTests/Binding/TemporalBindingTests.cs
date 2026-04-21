using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Binding;

public class TemporalBindingTests
{
	[Fact]
	public void TemporalCmd_compact_timespan_and_date_only()
	{
		var result = CliHostRunner.Run("temporal-cmd", "--duration", "5m", "--on", "2024-06-15");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("temporal:300:2024-06-15");
	}

	[Fact]
	public void TemporalCmd_standard_timespan_fallback()
	{
		var result = CliHostRunner.Run("temporal-cmd", "--duration", "01:30:00", "--on", "2024-01-01");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("temporal:5400:2024-01-01");
	}

	[Fact]
	public void ValidateTimeSpanRange_in_range_succeeds()
	{
		var result = CliHostRunner.Run("validate-timespan-range", "--window", "30m");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("ts-range:30");
	}

	[Fact]
	public void ValidateTimeSpanRange_out_of_range_exits_2()
	{
		var result = CliHostRunner.Run("validate-timespan-range", "--window", "3h");
		result.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(result))
			.Should().Contain("value must be between");
	}
}
