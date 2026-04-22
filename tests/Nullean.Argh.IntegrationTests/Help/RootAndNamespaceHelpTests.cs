using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class RootAndNamespaceHelpTests
{
	[Fact]
	public void RootHelp_does_not_list_nested_blob_command()
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
			  di-probe      Instance command type for DI resolution tests
			                (ArghServices.ServiceProvider).
			  storage       Commands under storage. Nested BlobCommands must be registered
			                explicitly via MapNamespace<BlobCommands>.
			  contentstack  Contentstack tree
			  labs          Labs tree

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
		var fromCommands = text.Substring(text.IndexOf("Commands:", StringComparison.Ordinal));
		fromCommands.Should().NotContain("blob");
	}

	[Fact]
	public void StorageHelp_lists_blob_namespace_and_not_flat_hello()
	{
		var result = CliHostRunner.Run(
			new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" },
			"storage",
			"--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		var expected = ($"""
			Usage: {CliHostPaths.CliHostAssemblyName} storage <command> [options]

			 (default command)
			   Integration-test default when only the storage namespace is selected.
			   Namespace default remarks for help layout tests.

			Global options:
			  --help, -h         Show help.
			  --verbose          

			'storage' options:
			  --prefix <string>  [required]

			Namespaces:
			  storage blob

			Commands:
			  storage list
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		text.Should().Be(expected);
		text.Should().NotContain("hello");
	}
}
