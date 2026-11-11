using AwesomeAssertions;
using Nullean.Argh.Tests.Fixtures;

namespace Nullean.Argh.Tests.Unit.Binding;

public class TryParseArghNegativeTests
{
	[Test]
	public void TryParseArghExact_unknown_flag_returns_false_for_deploy_record()
	{
		var ok = DeployCliArgs.TryParseArghExact(["--not-a-real-flag", "x"], out _);
		ok.Should().BeFalse();
	}

	[Test]
	public void TryParseArgh_unknown_flag_at_end_does_not_fail_and_extracts_known_flags()
	{
		var ok = DeployCliArgs.TryParseArgh(["--app-env", "prod", "--app-port", "8080", "--assume-cloned"], out var d);
		ok.Should().BeTrue();
		d!.Env.Should().Be("prod");
		d.Port.Should().Be(8080);
	}

	[Test]
	public void TryParseArgh_unknown_flag_with_value_in_middle_does_not_fail()
	{
		var ok = DeployCliArgs.TryParseArgh(["--unknown-flag", "somevalue", "--app-env", "staging", "--app-port", "443"], out var d);
		ok.Should().BeTrue();
		d!.Env.Should().Be("staging");
		d.Port.Should().Be(443);
	}

	[Test]
	public void TryParseArgh_mixed_unknown_and_known_flags_extracts_correctly()
	{
		var ok = DeployCliArgs.TryParseArgh(["assembler", "clone", "--app-env", "prod", "--app-port", "80", "--assume-cloned"], out var d);
		ok.Should().BeTrue();
		d!.Env.Should().Be("prod");
		d.Port.Should().Be(80);
	}
}
