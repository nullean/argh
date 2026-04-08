using FluentAssertions;
using Xunit;

namespace Nullean.Argh.Tests;

public class CliMoreTests
{
	// ── Parse error tests ────────────────────────────────────────────────────

	[Fact]
	public void ParseError_missing_required_flag_returns_exit_2()
	{
		var result = CliHostRunner.Run("hello");
		result.ExitCode.Should().Be(2);
	}

	[Fact]
	public void ParseError_missing_required_flag_stderr_has_Error()
	{
		var result = CliHostRunner.Run("hello");
		CliHostRunner.StderrText(result).Should().Contain("Error:");
	}

	[Fact]
	public void ParseError_missing_required_flag_stdout_has_Usage()
	{
		var result = CliHostRunner.Run("hello");
		CliHostRunner.StdoutText(result).Should().Contain("Usage:");
	}

	// ── bool? nullable flag tests ────────────────────────────────────────────

	[Fact]
	public void NullableBool_dry_run_flag_prints_true()
	{
		var result = CliHostRunner.Run("dry-run-cmd", "--dry-run");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dry-run:true");
	}

	[Fact]
	public void NullableBool_no_dry_run_flag_prints_false()
	{
		var result = CliHostRunner.Run("dry-run-cmd", "--no-dry-run");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dry-run:false");
	}

	[Fact]
	public void NullableBool_no_flag_prints_null()
	{
		var result = CliHostRunner.Run("dry-run-cmd");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dry-run:null");
	}

	// ── int parsing test ─────────────────────────────────────────────────────

	[Fact]
	public void Int_parses_and_echoes_count()
	{
		var result = CliHostRunner.Run("count-cmd", "--count", "42");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("count:42");
	}

	// ── FileInfo / DirectoryInfo / Uri tests ─────────────────────────────────

	[Fact]
	public void FileInfo_parses_and_echoes_file_name()
	{
		var result = CliHostRunner.Run("file-cmd", "--file", "/tmp/test.txt");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("file:test.txt");
	}

	[Fact]
	public void DirectoryInfo_parses_and_echoes_dir_name()
	{
		var result = CliHostRunner.Run("dir-cmd", "--dir", "/tmp/mydir");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dir:mydir");
	}

	[Fact]
	public void Uri_parses_and_echoes_host()
	{
		var result = CliHostRunner.Run("uri-cmd", "--uri", "https://example.com/path");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("uri:example.com");
	}

	// ── Custom parser test ───────────────────────────────────────────────────

	[Fact]
	public void CustomParser_point_parses_and_echoes_xy()
	{
		var result = CliHostRunner.Run("point-cmd", "--point", "3,4");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("point:3,4");
	}

	// ── Lambda command test ──────────────────────────────────────────────────

	[Fact]
	public void Lambda_command_invokes_and_echoes_msg()
	{
		var result = CliHostRunner.Run("lambda-cmd", "--msg", "hi");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("lambda:hi");
	}

	// ── Help content tests ───────────────────────────────────────────────────

	[Fact]
	public void HelloHelp_stdout_contains_name_option()
	{
		var result = CliHostRunner.Run("hello", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("--name <string>");
	}

	[Fact]
	public void HelloHelp_stdout_contains_usage_line()
	{
		var result = CliHostRunner.Run("hello", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("Usage:");
	}

	[Fact]
	public void HelloHelp_stdout_contains_options_section()
	{
		var result = CliHostRunner.Run("hello", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("Options:");
	}

	[Fact]
	public void HelloHelp_stdout_contains_help_flag()
	{
		var result = CliHostRunner.Run("hello", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("--help, -h");
	}

	// ── Root help boundary tests ─────────────────────────────────────────────

	[Fact]
	public void RootHelp_contains_storage_group()
	{
		var result = CliHostRunner.Run("--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("storage");
	}

	[Fact]
	public void RootHelp_does_not_contain_blob_subgroup()
	{
		var result = CliHostRunner.Run("--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().NotContain("blob");
	}

	[Fact]
	public void RootHelp_contains_hello_command()
	{
		var result = CliHostRunner.Run("--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("hello");
	}

	// ── Namespace help boundary tests ────────────────────────────────────────────

	[Fact]
	public void StorageHelp_contains_list_command()
	{
		var result = CliHostRunner.Run("storage", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("list");
	}

	[Fact]
	public void StorageHelp_contains_blob_subgroup()
	{
		var result = CliHostRunner.Run("storage", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("blob");
	}

	[Fact]
	public void StorageHelp_does_not_contain_hello_command()
	{
		var result = CliHostRunner.Run("storage", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().NotContain("hello");
	}

	// ── Enum help per-value tests ────────────────────────────────────────────

	[Fact]
	public void EnumHelp_contains_Red_value()
	{
		var result = CliHostRunner.Run("enum-cmd", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("Red");
	}

	[Fact]
	public void EnumHelp_contains_Blue_value()
	{
		var result = CliHostRunner.Run("enum-cmd", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("Blue");
	}

	[Fact]
	public void EnumHelp_color_option_shows_values_hint()
	{
		var result = CliHostRunner.Run("enum-cmd", "--help");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("[values: Red, Blue]");
	}

	// ── Completions tests ─────────────────────────────────────────────────────

	[Fact]
	public void Completions_zsh_exits_0_with_nonempty_stdout()
	{
		var result = CliHostRunner.Run("--completions", "zsh");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void Completions_fish_exits_0_with_nonempty_stdout()
	{
		var result = CliHostRunner.Run("--completions", "fish");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void Completions_zsh_stdout_contains_completion_keyword()
	{
		var result = CliHostRunner.Run("--completions", "zsh");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("complete");
	}

	[Fact]
	public void Completions_fish_stdout_contains_complete_command()
	{
		var result = CliHostRunner.Run("--completions", "fish");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Should().Contain("complete");
	}
}
