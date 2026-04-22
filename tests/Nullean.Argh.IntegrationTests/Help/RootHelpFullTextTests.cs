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
			  storage   Commands under storage. Nested BlobCommands must be registered
			            explicitly via MapNamespace<BlobCommands>.

			Commands:
			  hello                        Greet someone by name.
			  enum-cmd                     Enum and short options.
			  deploy                     
			  nullable-numeric-as-params 
			  optional-uri-as-params     
			  tags                       
			  dry-run-cmd                
			  count-cmd                  
			  file-cmd                   
			  dir-cmd                    
			  uri-cmd                    
			  temporal-cmd               
			  point-cmd                  
			  doc-lambda                   Documented handler for lambda-style Map (XML
			                               appears in help).
			  lambda-cmd                 
			  validate-range               Validate numeric range on --port.
			  validate-length              Validate string length on --name.
			  validate-regex               Validate regex pattern on --slug.
			  validate-allowed             Validate allowed values on --env.
			  validate-email               Validate email format on --address.
			  validate-uri-scheme          Validate URI scheme restriction on --endpoint.
			  validate-non-nullable-range  Validate numeric range on non-nullable --page-per
			                               with default.
			  validate-dto                 Validate DTO fields with range constraint.
			  validate-timespan-range      Validate TimeSpan inclusive range.
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		text.Should().Be(expected);
	}
}
