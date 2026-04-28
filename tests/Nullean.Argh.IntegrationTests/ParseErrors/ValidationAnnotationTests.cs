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
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(r));
		text.Should().Contain("[email]");
		text.Should().Contain("--address <email>");
	}

	[Fact]
	public void Nullable_optional_string_email_uses_same_email_placeholder_as_required()
	{
		var r = CliHostRunner.Run(NoColor, "validate-email-opt", "--help");
		r.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(r));
		text.Should().Contain("--mailbox <email>");
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
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(r));
		text.Should().Contain("[schemes: https]");
		text.Should().Contain("--endpoint <url>");
	}

	[Fact]
	public void Nullable_optional_uri_https_uses_same_url_placeholder_as_required()
	{
		var r = CliHostRunner.Run(NoColor, "validate-uri-scheme-opt", "--help");
		r.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(r));
		text.Should().Contain("--endpoint <url>");
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


	// ── filesystem path validations (FileInfo / DirectoryInfo) ─────────────

	[Fact]
	public void ExistingFile_existing_temp_file_succeeds()
	{
		var tmp = Path.GetTempFileName();
		try
		{
			var r = CliHostRunner.Run(NoColor, "validate-existing-file", "--file", tmp);
			r.ExitCode.Should().Be(0);
			ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain($"file:{Path.GetFullPath(tmp)}");
		}
		finally
		{
			try { File.Delete(tmp); }
			catch { /* ignore */ }
		}
	}

	[Fact]
	public void ExistingFile_missing_returns_exit_2()
	{
		var path = Path.Combine(Path.GetTempPath(), "argh-missing-existing-" + Guid.NewGuid());
		File.Exists(path).Should().BeFalse();
		var r = CliHostRunner.Run(NoColor, "validate-existing-file", "--file", path);
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r)).Should().Contain("Error: --file: file does not exist.");
	}

	[Fact]
	public void NonExistingFile_unused_path_succeeds()
	{
		var path = Path.Combine(Path.GetTempPath(), "argh-new-file-" + Guid.NewGuid());
		File.Exists(path).Should().BeFalse();
		try
		{
			var r =CliHostRunner.Run(NoColor, "validate-non-existing-file", "--path", path);
			r.ExitCode.Should().Be(0);
			ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain($"path:{Path.GetFullPath(path)}");
		}
		finally
		{
			if (File.Exists(path))
				File.Delete(path);
		}
	}

	[Fact]
	public void NonExistingFile_conflict_with_existing_returns_exit_2()
	{
		var tmp = Path.GetTempFileName();
		try
		{
			var r =CliHostRunner.Run(NoColor, "validate-non-existing-file", "--path", tmp);
			r.ExitCode.Should().Be(2);
			ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
				.Should().Contain("path already exists or is occupied by a directory.");
		}
		finally { try { File.Delete(tmp); } catch { } }
	}

	[Fact]
	public void ExistingDirectory_temp_directory_succeeds()
	{
		var dir = Path.Combine(Path.GetTempPath(), "argh-extdir-" + Guid.NewGuid());
		Directory.CreateDirectory(dir);
		try
		{
			var r =CliHostRunner.Run(NoColor, "validate-existing-directory", "--dir", dir);
			r.ExitCode.Should().Be(0);
			ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain($"dir:{Path.GetFullPath(dir)}");
		}
		finally { try { Directory.Delete(dir); } catch { } }
	}

	[Fact]
	public void ExistingDirectory_missing_returns_exit_2()
	{
		var path = Path.Combine(Path.GetTempPath(), "argh-missing-dir-" + Guid.NewGuid());
		Directory.Exists(path).Should().BeFalse();
		var r =CliHostRunner.Run(NoColor, "validate-existing-directory", "--dir", path);
		r.ExitCode.Should().Be(2);
		ConsoleOutput.Normalize(CliHostRunner.StderrText(r)).Should().Contain("Error: --dir: directory does not exist.");
	}

	[Fact]
	public void ExpandHomeFile_tilde_under_profile_succeeds()
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var leaf = "argh-expand-" + Guid.NewGuid().ToString("N");
		var abs = Path.Combine(home, leaf);
		File.WriteAllText(abs, "");
		try
		{
			var prefix = OperatingSystem.IsWindows() ? $"~{Path.DirectorySeparatorChar}" : "~/";
			var r = CliHostRunner.Run(NoColor, "validate-expand-home-file", "--file", prefix + leaf);
			r.ExitCode.Should().Be(0);
			ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain($"file:{Path.GetFullPath(abs)}");
		}
		finally { try { File.Delete(abs); } catch { } }
	}

	[Fact]
	public void NoSymlinkFile_regular_temp_file_succeeds()
	{
		var tmp = Path.GetTempFileName();
		try
		{
			var r =CliHostRunner.Run(NoColor, "validate-no-symlink-file", "--file", tmp);
			r.ExitCode.Should().Be(0);
			ConsoleOutput.Normalize(CliHostRunner.StdoutText(r)).Should().Contain("file:");
		}
		finally { try { File.Delete(tmp); } catch { } }
	}

	[Fact]
	public void NoSymlinkFile_symbolic_link_returns_exit_2()
	{
		var dir = Path.Combine(Path.GetTempPath(), "argh-symlink-" + Guid.NewGuid());
		Directory.CreateDirectory(dir);
		var target = Path.Combine(dir, "target.bin");
		var link = Path.Combine(dir, "link.bin");
		File.WriteAllText(target, "x");
		try
		{
			try
			{
				File.CreateSymbolicLink(link, target);
			}
			catch (IOException ex)
			{
				Console.WriteLine("skip symlink test: " + ex.Message);
				return;
			}

			var r =CliHostRunner.Run(NoColor, "validate-no-symlink-file", "--file", link);
			r.ExitCode.Should().Be(2);
			ConsoleOutput.Normalize(CliHostRunner.StderrText(r))
				.Should().Contain("must not be a symbolic link or reparse point.");
		}
		finally { try { Directory.Delete(dir, recursive: true); } catch { } }
	}

	[Fact]
	public void ExistingFile_help_lists_existence_annotation()
	{
		var r =CliHostRunner.Run(NoColor, "validate-existing-file", "--help");
		r.ExitCode.Should().Be(0);
		var text = ConsoleOutput.Normalize(CliHostRunner.StdoutText(r));
		text.Should().Contain("[existing]");
	}

}