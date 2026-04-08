using FluentAssertions;
using Xunit;

namespace Nullean.Argh.Tests;

[Collection("Console")]
public class CliMoreTests
{
    // ── Parse error tests ────────────────────────────────────────────────────

    [Fact]
    public async Task ParseError_missing_required_flag_returns_exit_2()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["hello"]));
        result.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task ParseError_missing_required_flag_stderr_has_Error()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["hello"]));
        result.Stderr.Should().Contain("Error:");
    }

    [Fact]
    public async Task ParseError_missing_required_flag_stdout_has_Usage()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["hello"]));
        result.Stdout.Should().Contain("Usage:");
    }

    // ── bool? nullable flag tests ────────────────────────────────────────────

    [Fact]
    public async Task NullableBool_dry_run_flag_prints_true()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["dry-run-cmd", "--dry-run"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Trim().Should().Be("dry-run:true");
    }

    [Fact]
    public async Task NullableBool_no_dry_run_flag_prints_false()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["dry-run-cmd", "--no-dry-run"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Trim().Should().Be("dry-run:false");
    }

    [Fact]
    public async Task NullableBool_no_flag_prints_null()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["dry-run-cmd"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Trim().Should().Be("dry-run:null");
    }

    // ── int parsing test ─────────────────────────────────────────────────────

    [Fact]
    public async Task Int_parses_and_echoes_count()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["count-cmd", "--count", "42"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Trim().Should().Be("count:42");
    }

    // ── FileInfo / DirectoryInfo / Uri tests ─────────────────────────────────

    [Fact]
    public async Task FileInfo_parses_and_echoes_file_name()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["file-cmd", "--file", "/tmp/test.txt"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Trim().Should().Be("file:test.txt");
    }

    [Fact]
    public async Task DirectoryInfo_parses_and_echoes_dir_name()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["dir-cmd", "--dir", "/tmp/mydir"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Trim().Should().Be("dir:mydir");
    }

    [Fact]
    public async Task Uri_parses_and_echoes_host()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["uri-cmd", "--uri", "https://example.com/path"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Trim().Should().Be("uri:example.com");
    }

    // ── Custom parser test ───────────────────────────────────────────────────

    [Fact]
    public async Task CustomParser_point_parses_and_echoes_xy()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["point-cmd", "--point", "3,4"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Trim().Should().Be("point:3,4");
    }

    // ── Lambda command test ──────────────────────────────────────────────────

    [Fact]
    public async Task Lambda_command_invokes_and_echoes_msg()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["lambda-cmd", "--msg", "hi"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Trim().Should().Be("lambda:hi");
    }

    // ── Help content tests ───────────────────────────────────────────────────

    [Fact]
    public async Task HelloHelp_stdout_contains_name_option()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["hello", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("--name <string>");
    }

    [Fact]
    public async Task HelloHelp_stdout_contains_usage_line()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["hello", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Usage:");
    }

    [Fact]
    public async Task HelloHelp_stdout_contains_options_section()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["hello", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Options:");
    }

    [Fact]
    public async Task HelloHelp_stdout_contains_help_flag()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["hello", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("--help, -h");
    }

    // ── Root help boundary tests ─────────────────────────────────────────────

    [Fact]
    public async Task RootHelp_contains_storage_group()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("storage");
    }

    [Fact]
    public async Task RootHelp_does_not_contain_blob_subgroup()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().NotContain("blob");
    }

    [Fact]
    public async Task RootHelp_contains_hello_command()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("hello");
    }

    // ── Namespace help boundary tests ────────────────────────────────────────────

    [Fact]
    public async Task StorageHelp_contains_list_command()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["storage", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("list");
    }

    [Fact]
    public async Task StorageHelp_contains_blob_subgroup()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["storage", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("blob");
    }

    [Fact]
    public async Task StorageHelp_does_not_contain_hello_command()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["storage", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().NotContain("hello");
    }

    // ── Enum help per-value tests ────────────────────────────────────────────

    [Fact]
    public async Task EnumHelp_contains_Red_value()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["enum-cmd", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Red");
    }

    [Fact]
    public async Task EnumHelp_contains_Blue_value()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["enum-cmd", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Blue");
    }

    [Fact]
    public async Task EnumHelp_color_option_shows_values_hint()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["enum-cmd", "--help"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("[values: Red, Blue]");
    }

    // ── Completions tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task Completions_zsh_exits_0_with_nonempty_stdout()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["--completions", "zsh"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Completions_fish_exits_0_with_nonempty_stdout()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["--completions", "fish"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Completions_zsh_stdout_contains_completion_keyword()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["--completions", "zsh"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("complete");
    }

    [Fact]
    public async Task Completions_fish_stdout_contains_complete_command()
    {
        var result = await ArghCli.RunWithCaptureAsync(() => ArghRuntime.RunAsync(["--completions", "fish"]));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("complete");
    }
}
