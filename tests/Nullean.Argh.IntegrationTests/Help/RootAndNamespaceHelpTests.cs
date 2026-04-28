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
			  -h, --help         Show help.
			  --version          Show version.
			  --verbose
			  --severity <enum>  Enum default for global-flag parsing regression. [default: Information]
			                     One of: <Trace|Information|Warning>

			Namespaces:
			  alias-followed  Root alias followed by additional mapped command classes.
			  alias-scope     Root-alias integration test namespace.
			  billing         Billing commands
			  storage         Commands under storage. Nested BlobCommands must be registered
			                  explicitly via MapNamespace<BlobCommands>.
			  support         Support commands

			Commands:
			  as-params-collection-syntax
			  as-params-optional-collection-syntax
			  as-params-referenced-dto
			  as-params-tag-set
			  as-params-with-ct
			  brace-doc                             Regression: braces in XML docs must not
			                                        become C# interpolation in generated
			                                        help.
			  color-set
			  count-cmd
			  deploy
			  dir-cmd
			  doc-lambda                            Documented handler for lambda-style Map
			                                        (XML appears in help).
			  dry-run-cmd
			  enum-cmd                              Enum and short options.
			  file-cmd
			  hello                                 Greet someone by name.
			  lambda-cmd
			  multi-enum-as-params
			  nullable-numeric-as-params
			  opt-tag-set
			  optional-uri-as-params
			  param-comment-record
			  ping
			  point-cmd
			  prop-doc-as-params
			  renamed-cmd
			  tag-set
			  tags
			  temporal-cmd
			  uri-cmd
			  validate-allowed                      Validate allowed values on --env.
			  validate-dto                          Validate DTO fields with range
			                                        constraint.
			  validate-email                        Validate email format on --address.
			  validate-email-opt                    Optional nullable mailbox (email).
			  validate-existing-directory           Require directory to exist.
			  validate-existing-directory-opt       Optional directory: [Existing] skips
			                                        when omitted.
			  validate-existing-file                Require path to reference an existing
			                                        file.
			  validate-expand-home-file             Expand ~ profile prefix before binding
			                                        FileInfo.
			  validate-length                       Validate string length on --name.
			  validate-no-symlink-file              Existing file that must not be a
			                                        symbolic link.
			  validate-no-symlink-file-opt          Optional file: [RejectSymbolicLinks]
			                                        skips when omitted.
			  validate-non-existing-file            Require path to reference a non-existing
			                                        file path.
			  validate-non-existing-file-opt        Optional file path: [NonExisting] skips
			                                        when omitted.
			  validate-non-nullable-range           Validate numeric range on non-nullable
			                                        --page-per with default.
			  validate-range                        Validate numeric range on --port.
			  validate-regex                        Validate regex pattern on --slug.
			  validate-timespan-range               Validate TimeSpan inclusive range.
			  validate-uri-scheme                   Validate URI scheme restriction on
			                                        --endpoint.
			  validate-uri-scheme-opt               Optional nullable HTTPS endpoint.
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
			  -h, --help         Show help.
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
