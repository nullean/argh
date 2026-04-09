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

			Namespaces:
			  di-probe
			  storage

			Commands:
			  hello    
			  enum-cmd    
			  deploy    
			  tags    
			  dry-run-cmd    
			  count-cmd    
			  file-cmd    
			  dir-cmd    
			  uri-cmd    
			  point-cmd    
			  doc-lambda    
			  lambda-cmd    

			Global options:
			  --verbose  
			  --help, -h  Show help.
			  --version  Show version.
			""").ReplaceLineEndings("\n").TrimEnd() + "\n";
		text.Should().Be(expected);
		text.Should().NotContain("blob");
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

			Namespaces:
			  storage blob

			Commands:
			  storage list    

			Global options:
			  --verbose          
			  --help, -h         Show help.

			'storage' options:
			  --prefix <string>  [required]

			""").ReplaceLineEndings("\n").TrimEnd() + "\n\n";
		text.Should().Be(expected);
		text.Should().NotContain("hello");
	}
}
