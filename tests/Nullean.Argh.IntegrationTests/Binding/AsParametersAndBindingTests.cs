using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Binding;

public class AsParametersAndBindingTests
{
	[Fact]
	public void Deploy_AsParameters_prefixed()
	{
		var result = CliHostRunner.Run("deploy", "--app-env", "prod", "--app-port", "8080");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("deploy:prod:8080");
	}

	[Fact]
	public void Tags_repeated_flags()
	{
		var result = CliHostRunner.Run("tags", "--tags", "a", "--tags", "b");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("tags:a,b");
	}
}
