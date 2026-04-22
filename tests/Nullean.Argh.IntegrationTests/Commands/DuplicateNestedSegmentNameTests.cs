using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Commands;

/// <summary>
/// Regression for AGH0022: nested <c>MapNamespace</c> under different parents may use the same segment when
/// parent configure lambdas are expression-bodied (nested invocation SpanStart equals parent lambda body start).
/// </summary>
public class DuplicateNestedSegmentNameTests
{
	[Fact]
	public void Same_nested_segment_under_distinct_parent_namespaces_routes_correctly()
	{
		var cs = CliHostRunner.Run("contentstack", "ai", "ping");
		CliHostRunner.StdoutText(cs).Trim().Should().Be("contentstack-ai-ping");

		var labs = CliHostRunner.Run("labs", "ai", "ping");
		CliHostRunner.StdoutText(labs).Trim().Should().Be("labs-ai-ping");
	}
}
