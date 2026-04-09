using FluentAssertions;
using Nullean.Argh.Runtime;
using Nullean.Argh.Tests.Fixtures;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Filters;

[Collection("Console")]
public class FilterPipelineInProcTests
{
	[Fact]
	public async Task RunAsync_global_and_per_command_filters_run()
	{
		TestsGlobalFilter.InvokeCount = 0;
		TestsPerCommandFilter.InvokeCount = 0;
		var code = await ArghRuntime.RunAsync(["hello", "--name", "t"]);
		code.Should().Be(0);
		TestsGlobalFilter.InvokeCount.Should().Be(1);
		TestsPerCommandFilter.InvokeCount.Should().Be(1);
	}
}
