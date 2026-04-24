using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class RootAndNamespaceHelpTests
{
	private static string TrimLines(string s) =>
		string.Join("\n", s.Split('\n').Select(l => l.TrimEnd()));

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
			  --help, -h         Show help.
			  --version          Show version.
			  --verbose
			  --severity <enum>  Enum default for global-flag parsing regression. [default: Information]
			                     One of: <Trace|Information|Warning>

			Namespaces:
			  storage      Commands under storage. Nested BlobCommands must be registered
			               explicitly via MapNamespace<BlobCommands>.
			  billing      Billing commands
			  support      Support commands
			  alias-scope  Root-alias integration test namespace.

			Commands:
			  hello                        Greet someone by name.
			  enum-cmd                     Enum and short options.
			  deploy
			  as-params-with-ct
			  nullable-numeric-as-params
			  multi-enum-as-params
			  optional-uri-as-params
			  prop-doc-as-params
			  param-comment-record
			  tags
			  tag-set
			  color-set
			  opt-tag-set
			  as-params-tag-set
			  brace-doc                    Regression: braces in XML docs must not become C#
			                               interpolation in generated help.
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
			  ping
			  renamed-cmd
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		TrimLines(text).Should().Be(TrimLines(expected));
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
			  --severity <enum>  Enum default for global-flag parsing regression. [default: Information]
			                     One of: <Trace|Information|Warning>

			'storage' options:
			  --prefix <string>  [default: ]

			Namespaces:
			  storage blob

			Commands:
			  storage list
			""").ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";
		TrimLines(text).Should().Be(TrimLines(expected));
		text.Should().NotContain("hello");
	}
}
