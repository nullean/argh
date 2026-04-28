using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Help;

public class AsParametersHelpDocumentationTests
{
	private static readonly IReadOnlyDictionary<string, string> NoColor =
		new Dictionary<string, string>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" };

	[Fact]
	public void AsParameters_init_property_summaries_appear_in_command_help()
	{
		var result = CliHostRunner.Run(NoColor, "prop-doc-as-params", "--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("Argh_help_doc_alpha_unique");
		text.Should().Contain("Argh_help_doc_beta_unique");
	}

	[Fact]
	public void AsParameters_record_positional_parameter_summary_appears_in_command_help()
	{
		var result = CliHostRunner.Run(NoColor, "param-comment-record", "--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("Argh_help_doc_gamma_unique");
	}

	[Fact]
	public void PropDocAsParams_binds()
	{
		var result = CliHostRunner.Run("prop-doc-as-params", "--alpha", "x", "--beta", "3");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("prop-doc:x:3");
	}

	[Fact]
	public void AsParameters_referenced_project_property_summaries_appear_in_command_help()
	{
		var result = CliHostRunner.Run(NoColor, "as-params-referenced-dto", "--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("Documentation root. Defaults to cwd/docs.");
		text.Should().Contain("Output directory. Defaults to .artifacts/html.");
	}
}
