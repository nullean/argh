using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

/// <summary>Asserts stable root help (NO_COLOR, normalized); assembly name via <see cref="CliHostPaths.CliHostAssemblyName"/>.</summary>
public class RootHelpFullTextTests
{
	[Fact]
	public void Root_help_full_text_matches_expected_shape()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		var expected = ($"""
			Usage: {CliHostPaths.CliHostAssemblyName} <namespace|command> [options]

			 (default command)
			   Integration-test default when no subcommand is given at the app root.
			   Root default remarks for help layout tests.

			Global options:
			  --help, -h  Show help.
			  --version   Show version.
			  --verbose   

			Namespaces:
			  di-probe  Instance command type for DI resolution tests
			            (ArghServices.ServiceProvider).
			  storage   Commands under storage; nested class becomes storage blob nested
			            namespace.

			Commands:
			  hello        Greet someone by name.
			  enum-cmd     Enum and short options.
			  deploy     
			  tags       
			  dry-run-cmd
			  count-cmd  
			  file-cmd   
			  dir-cmd    
			  uri-cmd    
			  point-cmd  
			  doc-lambda   Documented handler for lambda-style Map (XML appears in help).
			  lambda-cmd 
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		text.Should().Be(expected);
	}
}
