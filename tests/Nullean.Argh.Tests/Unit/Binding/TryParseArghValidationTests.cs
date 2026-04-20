using FluentAssertions;
using Nullean.Argh.Tests.Fixtures;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Binding;

[Collection("Console")]
public class TryParseArghValidationTests
{
	[Fact]
	public void TryParseArgh_range_in_bounds_returns_true()
	{
		var ok = ValidatedDtoArgs.TryParseArgh(["--count", "50"], out var d);
		ok.Should().BeTrue();
		d!.Count.Should().Be(50);
	}

	[Fact]
	public void TryParseArgh_range_out_of_bounds_returns_false()
	{
		var ok = ValidatedDtoArgs.TryParseArgh(["--count", "150"], out var d);
		ok.Should().BeFalse();
		d.Should().BeNull();
	}

	[Fact]
	public void TryParseArgh_range_zero_returns_false()
	{
		var ok = ValidatedDtoArgs.TryParseArgh(["--count", "0"], out var d);
		ok.Should().BeFalse();
		d.Should().BeNull();
	}
}
