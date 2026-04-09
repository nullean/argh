using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class EnumHelpTests
{
	[Fact]
	public void Enum_cmd_help_lists_values()
	{
		var result = CliHostRunner.Run("enum-cmd", "--help");
		result.ExitCode.Should().Be(0);
		var o = CliHostRunner.StdoutText(result);
		o.Should().Contain("Red");
		o.Should().Contain("Blue");
		o.Should().Contain("[values: Red, Blue]");
	}
}
