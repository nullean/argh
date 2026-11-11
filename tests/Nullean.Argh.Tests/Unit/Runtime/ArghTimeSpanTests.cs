using AwesomeAssertions;
using Nullean.Argh;

namespace Nullean.Argh.Tests.Unit.Runtime;

public class ArghTimeSpanTests
{
	[Test]
	[Arguments("1s", 1)]
	[Arguments("5m", 5 * 60)]
	[Arguments("2h", 2 * 3600)]
	[Arguments("3d", 3 * 86400)]
	public void TryParse_compact_units(string raw, double expectedTotalSeconds)
	{
		ArghTimeSpan.TryParse(raw, out var ts).Should().BeTrue();
		ts.TotalSeconds.Should().Be(expectedTotalSeconds);
	}

	[Test]
	public void TryParse_standard_invariant_fallback()
	{
		ArghTimeSpan.TryParse("1.00:00:00", out var ts).Should().BeTrue();
		ts.Should().Be(TimeSpan.FromDays(1));
	}

	[Test]
	public void TryParse_fraction_before_suffix_falls_back_to_TimeSpan_try_parse_or_fails()
	{
		// Not valid compact (digit.fraction); may fail or parse via standard TimeSpan if any
		ArghTimeSpan.TryParse("1.5h", out _).Should().BeFalse();
	}
}
