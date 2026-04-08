using FluentAssertions;
using Xunit;

namespace Nullean.Argh.Tests;

public class CommandLineSplittingTests
{
	[Fact]
	public void SplitCommandLine_splits_on_whitespace()
	{
		ArghCli.SplitCommandLine("a b").Should().Equal("a", "b");
	}

	[Fact]
	public void SplitCommandLine_groups_quoted_segments()
	{
		ArghCli.SplitCommandLine("one  \"two three\"  four").Should().Equal("one", "two three", "four");
	}

	[Fact]
	public void SplitCommandLine_empty_and_whitespace_returns_empty()
	{
		ArghCli.SplitCommandLine("").Should().BeEmpty();
		ArghCli.SplitCommandLine("   ").Should().BeEmpty();
	}
}
