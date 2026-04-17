using FluentAssertions;
using Nullean.Argh.Tests.Fixtures;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Binding;

public class TryParseArghNegativeTests
{
	[Fact]
	public void TryParseArgh_unknown_flag_returns_false_for_deploy_record()
	{
		var ok = DeployCliArgs.TryParseArgh(["--not-a-real-flag", "x"], out _);
		ok.Should().BeFalse();
	}
}
