using AwesomeAssertions;
using Nullean.Argh.Tests.Fixtures;

namespace Nullean.Argh.Tests.Unit.Binding;

[NotInParallel]
public class TryParseArghValidationTests
{
	[Test]
	public void TryParseArgh_range_in_bounds_returns_true()
	{
		var ok = ValidatedDtoArgs.TryParseArgh(["--count", "50"], out var d);
		ok.Should().BeTrue();
		d!.Count.Should().Be(50);
	}

	[Test]
	public void TryParseArgh_range_out_of_bounds_returns_false()
	{
		var ok = ValidatedDtoArgs.TryParseArgh(["--count", "150"], out var d);
		ok.Should().BeFalse();
		d.Should().BeNull();
	}

	[Test]
	public void TryParseArgh_range_zero_returns_false()
	{
		var ok = ValidatedDtoArgs.TryParseArgh(["--count", "0"], out var d);
		ok.Should().BeFalse();
		d.Should().BeNull();
	}
}
