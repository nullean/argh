using FluentAssertions;
using Nullean.Argh;
using Xunit;

namespace Nullean.Argh.Tests;

public class FuzzyMatchTests
{
	[Fact]
	public void LevenshteinDistance_empty_strings_is_zero()
	{
		FuzzyMatch.LevenshteinDistance("", "").Should().Be(0);
	}

	[Fact]
	public void LevenshteinDistance_empty_to_non_empty_is_length()
	{
		FuzzyMatch.LevenshteinDistance("", "abc").Should().Be(3);
		FuzzyMatch.LevenshteinDistance("abc", "").Should().Be(3);
	}

	[Fact]
	public void LevenshteinDistance_equal_strings_is_zero()
	{
		FuzzyMatch.LevenshteinDistance("hello", "hello").Should().Be(0);
	}

	[Fact]
	public void LevenshteinDistance_kitten_sitting_is_three()
	{
		FuzzyMatch.LevenshteinDistance("kitten", "sitting").Should().Be(3);
	}

	[Fact]
	public void LevenshteinDistance_null_a_throws()
	{
		var act = () => FuzzyMatch.LevenshteinDistance(null!, "x");
		act.Should().Throw<ArgumentNullException>().WithParameterName("a");
	}

	[Fact]
	public void LevenshteinDistance_null_b_throws()
	{
		var act = () => FuzzyMatch.LevenshteinDistance("x", null!);
		act.Should().Throw<ArgumentNullException>().WithParameterName("b");
	}

	[Fact]
	public void FindClosest_returns_empty_when_no_candidate_within_cap()
	{
		var r = FuzzyMatch.FindClosest("abc", new[] { "xyz", "qqq" }, maxDistance: 1);
		r.Should().BeEmpty();
	}

	[Fact]
	public void FindClosest_returns_single_candidate_when_unique_minimum()
	{
		var r = FuzzyMatch.FindClosest("run", new[] { "run", "runs", "running" }, maxDistance: 0);
		r.Should().ContainSingle().Which.Should().Be("run");
	}

	[Fact]
	public void FindClosest_includes_multiple_matches_at_same_distance()
	{
		var r = FuzzyMatch.FindClosest("cat", new[] { "bat", "hat", "dog" }, maxDistance: 1);
		r.Should().BeEquivalentTo(new[] { "bat", "hat" }, o => o.WithStrictOrdering());
	}

	[Fact]
	public void FindClosest_null_input_throws()
	{
		var act = () => FuzzyMatch.FindClosest(null!, Array.Empty<string>(), 0);
		act.Should().Throw<ArgumentNullException>().WithParameterName("input");
	}

	[Fact]
	public void FindClosest_null_candidates_throws()
	{
		var act = () => FuzzyMatch.FindClosest("a", null!, 0);
		act.Should().Throw<ArgumentNullException>().WithParameterName("candidates");
	}

	[Fact]
	public void FindClosest_negative_maxDistance_throws()
	{
		var act = () => FuzzyMatch.FindClosest("a", new[] { "a" }, -1);
		act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxDistance");
	}

	[Fact]
	public void FindClosest_skips_null_candidates()
	{
		var r = FuzzyMatch.FindClosest("ab", new[] { null!, "ab", null! }, maxDistance: 0);
		r.Should().ContainSingle().Which.Should().Be("ab");
	}
}
