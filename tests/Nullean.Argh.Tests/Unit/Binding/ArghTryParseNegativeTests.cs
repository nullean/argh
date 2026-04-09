using FluentAssertions;
using Nullean.Argh.Tests.Fixtures;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Binding;

public class ArghTryParseNegativeTests
{
	[Fact]
	public void ArghTryParse_unknown_flag_returns_false_for_deploy_record()
	{
		var ok = DeployCliArgs.ArghTryParse(["--not-a-real-flag", "x"], out _);
		ok.Should().BeFalse();
	}
}
