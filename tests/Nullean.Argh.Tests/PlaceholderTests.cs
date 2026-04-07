using FluentAssertions;
using Xunit;

namespace Nullean.Argh.Tests;

public class PlaceholderTests
{
	[Fact]
	public void Placeholder() => true.Should().BeTrue();
}
