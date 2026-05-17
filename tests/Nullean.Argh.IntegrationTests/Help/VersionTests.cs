using FluentAssertions;
using System.Reflection;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class VersionTests
{
	[Fact]
	public void Version_stdout_matches_cli_host_informational_version()
	{
		var dll = Path.Combine(AppContext.BaseDirectory, CliHostPaths.CliHostDllFileName);
		var asm = Assembly.LoadFrom(dll);
		var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		          ?? asm.GetName().Version!.ToString();
		var result = CliHostRunner.Run("--version");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be(ver);
	}
}
