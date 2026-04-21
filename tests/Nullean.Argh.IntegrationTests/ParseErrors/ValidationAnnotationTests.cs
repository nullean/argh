using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.ParseErrors;

public class ValidationAnnotationTests
{
	private static readonly Dictionary<string, string> NoColor =
		new(StringComparer.Ordinal) { ["NO_COLOR"] = "1" };

	// ── [Range] ──────────────────────────────────────────────────────────────

	[Fact]
	public void Range_valid_value_succeeds()
	{
		var r = CliHostRunner.Run(NoColor, "validate-range", "--port", "8080");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("port:8080");
	}

	[Fact]
	public void Range_too_large_returns_exit_2_with_error()
	{
		var r = CliHostRunner.Run(NoColor, "validate-range", "--port", "99999");
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("Error: --port: value must be between 1 and 65535.");
	}

	[Fact]
	public void Range_zero_returns_exit_2_with_error()
	{
		var r = CliHostRunner.Run(NoColor, "validate-range", "--port", "0");
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("Error: --port: value must be between 1 and 65535.");
	}

	[Fact]
	public void Range_error_includes_run_hint()
	{
		var r = CliHostRunner.Run(NoColor, "validate-range", "--port", "0");
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("--help' for usage.");
	}

	[Fact]
	public void Range_help_shows_constraint_inline()
	{
		var r = CliHostRunner.Run(NoColor, "validate-range", "--help");
		r.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(r));
		text.Should().Contain("[range: 1–65535]");
	}

	// ── [StringLength] ───────────────────────────────────────────────────────

	[Fact]
	public void StringLength_valid_value_succeeds()
	{
		var r = CliHostRunner.Run(NoColor, "validate-length", "--name", "hi");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("name:hi");
	}

	[Fact]
	public void StringLength_too_short_returns_exit_2()
	{
		var r = CliHostRunner.Run(NoColor, "validate-length", "--name", "x");
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("Error: --name: value must be between 2 and 100 characters.");
	}

	[Fact]
	public void StringLength_help_shows_constraint()
	{
		var r = CliHostRunner.Run(NoColor, "validate-length", "--help");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("[length: 2–100]");
	}

	// ── [RegularExpression] ──────────────────────────────────────────────────

	[Fact]
	public void Regex_valid_value_succeeds()
	{
		var r = CliHostRunner.Run(NoColor, "validate-regex", "--slug", "hello-world");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("slug:hello-world");
	}

	[Fact]
	public void Regex_invalid_value_returns_exit_2()
	{
		var r = CliHostRunner.Run(NoColor, "validate-regex", "--slug", "Hello World!");
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("Error: --slug: value does not match required pattern");
	}

	[Fact]
	public void Regex_help_shows_pattern()
	{
		var r = CliHostRunner.Run(NoColor, "validate-regex", "--help");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("[pattern:");
	}

	// ── [AllowedValues] ──────────────────────────────────────────────────────

	[Fact]
	public void AllowedValues_valid_value_succeeds()
	{
		var r = CliHostRunner.Run(NoColor, "validate-allowed", "--env", "dev");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("env:dev");
	}

	[Fact]
	public void AllowedValues_disallowed_value_returns_exit_2()
	{
		var r = CliHostRunner.Run(NoColor, "validate-allowed", "--env", "production");
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("Error: --env: value must be one of: dev, staging, prod.");
	}

	[Fact]
	public void AllowedValues_help_shows_allowed_values()
	{
		var r = CliHostRunner.Run(NoColor, "validate-allowed", "--help");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("[allowed: dev|staging|prod]");
	}

	// ── [EmailAddress] ───────────────────────────────────────────────────────

	[Fact]
	public void Email_valid_value_succeeds()
	{
		var r = CliHostRunner.Run(NoColor, "validate-email", "--address", "user@example.com");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("email:user@example.com");
	}

	[Fact]
	public void Email_invalid_value_returns_exit_2()
	{
		var r = CliHostRunner.Run(NoColor, "validate-email", "--address", "notanemail");
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("Error: --address: value is not a valid email address.");
	}

	[Fact]
	public void Email_help_shows_email_constraint()
	{
		var r = CliHostRunner.Run(NoColor, "validate-email", "--help");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("[email]");
	}

	// ── [UriScheme] ──────────────────────────────────────────────────────────

	[Fact]
	public void UriScheme_https_succeeds()
	{
		var r = CliHostRunner.Run(NoColor, "validate-uri-scheme", "--endpoint", "https://example.com");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("scheme:https");
	}

	[Fact]
	public void UriScheme_http_rejected_returns_exit_2()
	{
		var r = CliHostRunner.Run(NoColor, "validate-uri-scheme", "--endpoint", "http://example.com");
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("Error: --endpoint: URI scheme must be one of: https.");
	}

	[Fact]
	public void UriScheme_help_shows_schemes_constraint()
	{
		var r = CliHostRunner.Run(NoColor, "validate-uri-scheme", "--help");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("[schemes: https]");
	}

	// ── [Range] on non-nullable value type with default ─────────────────────

	[Fact]
	public void NonNullableRange_default_value_succeeds()
	{
		var r = CliHostRunner.Run(NoColor, "validate-non-nullable-range");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("page-per:20");
	}

	[Fact]
	public void NonNullableRange_valid_value_succeeds()
	{
		var r = CliHostRunner.Run(NoColor, "validate-non-nullable-range", "--page-per", "50");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("page-per:50");
	}

	[Fact]
	public void NonNullableRange_negative_returns_exit_2()
	{
		var r = CliHostRunner.Run(NoColor, "validate-non-nullable-range", "--page-per", "-1");
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("Error: --page-per: value must be between 0 and");
	}

	// ── [AsParameters] + [Range] ─────────────────────────────────────────────

	[Fact]
	public void Dto_range_valid_value_succeeds()
	{
		var r = CliHostRunner.Run(NoColor, "validate-dto", "--count", "50");
		r.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("dto:50");
	}

	[Fact]
	public void Dto_range_out_of_range_returns_exit_2()
	{
		var r = CliHostRunner.Run(NoColor, "validate-dto", "--count", "150");
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
			.Should().Contain("Error: --count: value must be between 1 and 100.");
	}
}
