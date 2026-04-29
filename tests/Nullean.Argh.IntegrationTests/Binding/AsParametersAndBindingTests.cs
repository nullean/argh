using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Binding;

public class AsParametersAndBindingTests
{
	[Fact]
	public void Deploy_AsParameters_prefixed()
	{
		var result = CliHostRunner.Run("deploy", "--app-env", "prod", "--app-port", "8080");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("deploy:prod:8080");
	}

	[Fact]
	public void AsParameters_DTO_includes_injected_CancellationToken_from_runtime()
	{
		var result = CliHostRunner.Run("as-params-with-ct", "--run-env", "prod", "--run-port", "8080");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("as-params-ct:prod:8080:True");
	}

	[Fact]
	public void Nullable_numeric_AsParameters_binds_prefixed_flags()
	{
		var result = CliHostRunner.Run(
			"nullable-numeric-as-params", "--labs-rps", "12", "--labs-max-pages", "3");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("nullable-numeric:12:3");
	}

	[Fact]
	public void Optional_Uri_AsParameters_omitted_flag_binds_null()
	{
		var result = CliHostRunner.Run("optional-uri-as-params");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("optional-uri:null");
	}

	[Fact]
	public void Optional_Uri_AsParameters_passed_flag_binds_value()
	{
		var result = CliHostRunner.Run("optional-uri-as-params", "--endpoint", "https://example.com/path");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("optional-uri:https://example.com/path");
	}

	[Fact]
	public void Tags_repeated_flags()
	{
		var result = CliHostRunner.Run("tags", "--tags", "a", "--tags", "b");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("tags:a,b");
	}

	[Fact]
	public void AsParameters_CollectionSyntax_property_target_binds_supported_collections()
	{
		var result = CliHostRunner.Run(
			"as-params-collection-syntax",
			"--cs-tag-ids", "3,1,2",
			"--cs-labels", "alpha|beta",
			"--cs-ports", "80;443");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("as-params-collection-syntax:1,2,3:alpha,beta:80,443");
	}

	[Fact]
	public void AsParameters_CollectionSyntax_property_target_rejects_readonlyset_duplicates()
	{
		var result = CliHostRunner.Run("as-params-collection-syntax", "--cs-tag-ids", "1,1", "--cs-labels", "x", "--cs-ports", "80");
		result.ExitCode.Should().Be(2);
		CliHostRunner.StderrText(result).Should().Contain("duplicate value");
	}

	[Fact]
	public void AsParameters_optional_init_property_with_CollectionSyntax_omitted_flag_binds_null()
	{
		var result = CliHostRunner.Run("as-params-optional-collection-syntax");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("as-params-optional-collection-syntax:null:null");
	}

	[Fact]
	public void AsParameters_optional_init_property_with_CollectionSyntax_parses_csv_values()
	{
		var result = CliHostRunner.Run(
			"as-params-optional-collection-syntax",
			"--ocs-tag-ids", "3,1,2",
			"--ocs-output", "dist");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("as-params-optional-collection-syntax:1,2,3:dist");
	}

	[Fact]
	public void AsParameters_referenced_project_type_binds_flags()
	{
		var result = CliHostRunner.Run(
			"as-params-referenced-dto",
			"--path", "docs",
			"--output", ".artifacts/site");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("as-params-referenced:docs:.artifacts/site");
	}
}
