namespace Nullean.Argh.IntegrationTests.Infrastructure;

/// <summary>Expected stdout/stderr from <see cref="CliHostRunner"/> when <c>NO_COLOR=1</c> (no ANSI); matches generated help/error shape.</summary>
internal static class CliHostGoldenOutput
{
	internal static string RootHelpNoColor() =>
		($"""
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

	internal static string HelloHelpNoColor() =>
		($"""
		Usage: {CliHostPaths.CliHostAssemblyName} hello --name <string>

		Global options:
		  --verbose        
		  --help, -h       Show help.

		Options:
		  --name <string>  [required]
		""").ReplaceLineEndings("\n").TrimEnd() + "\n";

	/// <summary>Storage namespace help ends with an extra blank line (matches console output).</summary>
	internal static string StorageHelpNoColor() =>
		($"""
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

	internal static string EnumCmdHelpNoColor() =>
		($"""
		Usage: {CliHostPaths.CliHostAssemblyName} enum-cmd --color <string> --name <string>

		Global options:
		  --verbose         
		  --help, -h        Show help.

		Options:
		  --color <string>  [required] [values: Red, Blue]
		  --name <string>   [required]
		""").ReplaceLineEndings("\n").TrimEnd() + "\n";

	internal static string DocLambdaHelpNoColor() =>
		($"""
		Usage: {CliHostPaths.CliHostAssemblyName} doc-lambda --line <string>

		Global options:
		  --verbose        
		  --help, -h       Show help.

		Options:
		  --line <string>  [required]
		""").ReplaceLineEndings("\n").TrimEnd() + "\n";

	internal static string HelloMissingRequiredFlagStdout() =>
		($"""
		Usage: {CliHostPaths.CliHostAssemblyName} hello --name <string>

		Global options:
		  --verbose        
		  --help, -h       Show help.

		Options:
		  --name <string>  [required]
		""").ReplaceLineEndings("\n").TrimEnd() + "\n";

	internal static string HelloMissingRequiredFlagStderr() => "Error: missing required flag --name.\n";
}
