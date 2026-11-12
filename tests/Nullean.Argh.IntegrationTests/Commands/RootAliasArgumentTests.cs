using AwesomeAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using ProcNet;

namespace Nullean.Argh.IntegrationTests.Commands;

/// <summary>
/// Subprocess tests against <c>Nullean.Argh.Tests.RootArgumentHost</c>: an app-root <c>MapAndRootAlias</c> whose handler
/// starts with an <c>[Argument]</c>, next to a named command and a namespace whose <c>MapRoot</c> takes one too (#138).
/// </summary>
public class RootAliasArgumentTests
{
	private static void ShouldRun(ProcessCaptureResult result, string expectedStdout)
	{
		CliHostRunner.StderrText(result).Should().BeEmpty();
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be(expectedStdout);
	}

	[Test]
	public void Bare_positional_reaches_the_root_alias() =>
		ShouldRun(CliHostRunner.RunRootArgumentHost("pkg.nupkg"), "marker:validate path=pkg.nupkg version=null skip=False");

	[Test]
	public void Bare_positional_behaves_like_the_named_command() =>
		ShouldRun(CliHostRunner.RunRootArgumentHost("validate", "pkg.nupkg"), "marker:validate path=pkg.nupkg version=null skip=False");

	[Test]
	public void Positional_then_flags_reach_the_root_alias() =>
		ShouldRun(CliHostRunner.RunRootArgumentHost("pkg.nupkg", "-r", "-v", "1.2"), "marker:validate path=pkg.nupkg version=1.2 skip=True");

	[Test]
	public void Leading_short_bool_flag_of_the_root_alias_is_not_reported_as_unknown() =>
		ShouldRun(CliHostRunner.RunRootArgumentHost("-r", "pkg.nupkg"), "marker:validate path=pkg.nupkg version=null skip=True");

	[Test]
	public void Leading_short_value_flag_of_the_root_alias_is_not_reported_as_unknown() =>
		ShouldRun(CliHostRunner.RunRootArgumentHost("-v", "1.2", "pkg.nupkg"), "marker:validate path=pkg.nupkg version=1.2 skip=False");

	[Test]
	public void Leading_short_flag_with_equals_of_the_root_alias_is_not_reported_as_unknown() =>
		ShouldRun(CliHostRunner.RunRootArgumentHost("-v=1.2", "pkg.nupkg"), "marker:validate path=pkg.nupkg version=1.2 skip=False");

	[Test]
	public void Leading_long_flags_of_the_root_alias_are_not_reported_as_unknown() =>
		ShouldRun(
			CliHostRunner.RunRootArgumentHost("--skip-release-mode", "--expected-version", "1.2", "pkg.nupkg"),
			"marker:validate path=pkg.nupkg version=1.2 skip=True"
		);

	[Test]
	public void Unknown_leading_short_flag_is_still_an_error()
	{
		var result = CliHostRunner.RunRootArgumentHost("-z", "pkg.nupkg");
		result.ExitCode.Should().Be(2);
		CliHostRunner.StderrText(result).Should().Contain("unknown short option '-z'");
		CliHostRunner.StdoutText(result).Should().NotContain("marker:");
	}

	[Test]
	public void Named_command_still_wins_over_the_positional() => ShouldRun(CliHostRunner.RunRootArgumentHost("inspect"), "marker:inspect");

	[Test]
	public void Command_names_match_case_insensitively_before_the_positional() => ShouldRun(CliHostRunner.RunRootArgumentHost("INSPECT"), "marker:inspect");

	[Test]
	public void Namespace_command_still_wins_over_the_namespace_root_positional() =>
		ShouldRun(CliHostRunner.RunRootArgumentHost("feed", "list"), "marker:feed-list");

	[Test]
	public void Bare_positional_reaches_the_namespace_root() =>
		ShouldRun(CliHostRunner.RunRootArgumentHost("feed", "nuget.org"), "marker:feed-root name=nuget.org");

	[Test]
	public void Root_help_still_prints()
	{
		var result = CliHostRunner.RunRootArgumentHost("--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("(default command)");
		text.Should().Contain("inspect");
	}

	[Test]
	public void Version_still_prints()
	{
		var result = CliHostRunner.RunRootArgumentHost("--version");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().NotBeEmpty();
		CliHostRunner.StdoutText(result).Should().NotContain("marker:");
	}

	[Test]
	public void Schema_token_is_not_taken_as_the_positional()
	{
		var result = CliHostRunner.RunRootArgumentHost("__schema");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("\"commands\"");
		CliHostRunner.StdoutText(result).Should().NotContain("marker:");
	}

	[Test]
	public void Completion_token_is_not_taken_as_the_positional()
	{
		var result = CliHostRunner.RunRootArgumentHost("__complete", "bash", "--", "ins");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("inspect");
		CliHostRunner.StdoutText(result).Should().NotContain("marker:");
	}

	[Test]
	public void Argument_help_drops_the_flag_synopsis_prefix()
	{
		var result = CliHostRunner.RunRootArgumentHost("validate", "--help");
		result.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(result));
		text.Should().Contain("Path to the package to validate.");
		text.Should().NotContain("-, --path");
		text.Should().NotContain("--path,");
	}

	[Test]
	public void Argument_schema_description_drops_the_flag_synopsis_prefix()
	{
		var result = CliHostRunner.RunRootArgumentHost("__schema");
		result.ExitCode.Should().Be(0);
		var json = CliHostRunner.StdoutText(result);
		json.Should().Contain("Path to the package to validate.");
		json.Should().NotContain("-, --path");
	}
}
