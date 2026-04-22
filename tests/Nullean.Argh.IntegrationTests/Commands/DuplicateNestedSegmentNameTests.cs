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
		var billing = CliHostRunner.Run("billing", "tools", "status");
		CliHostRunner.StdoutText(billing).Trim().Should().Be("billing-tools-status");

		var support = CliHostRunner.Run("support", "tools", "status");
		CliHostRunner.StdoutText(support).Trim().Should().Be("support-tools-status");
	}
}
