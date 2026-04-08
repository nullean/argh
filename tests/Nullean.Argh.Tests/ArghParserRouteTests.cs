using FluentAssertions;
using Nullean.Argh.Runtime;
using Xunit;

namespace Nullean.Argh.Tests;

[Collection("Console")]
public class ArghParserRouteTests
{
	[Fact]
	public void Route_resolves_flat_command_and_remaining_arguments()
	{
		var r = ArghParser.Route(["hello", "--name", "x"]);
		r.Should().NotBeNull();
		r!.Value.CommandPath.Should().Be("hello");
		r.Value.RemainingArgs.Should().Equal("--name", "x");
	}

	[Fact]
	public void Route_resolves_group_command_and_remaining_arguments()
	{
		var r = ArghParser.Route(["storage", "list", "--verbose"]);
		r.Should().NotBeNull();
		r!.Value.CommandPath.Should().Be("storage/list");
		r.Value.RemainingArgs.Should().Equal("--verbose");
	}

	[Fact]
	public void Route_resolves_nested_group_command_path()
	{
		var r = ArghParser.Route(["storage", "blob", "upload"]);
		r.Should().NotBeNull();
		r!.Value.CommandPath.Should().Be("storage/blob/upload");
		r.Value.RemainingArgs.Should().BeEmpty();
	}

	[Fact]
	public void Route_returns_null_for_root_help() => ArghParser.Route(["--help"]).Should().BeNull();
}
