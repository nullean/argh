using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Commands;

public class CommandOutputTests
{
	[Fact]
	public void Hello_ok_marker()
	{
		var result = CliHostRunner.Run("hello", "--name", "test");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("ok:test");
	}

	[Fact]
	public void NullableBool_dry_run_true()
	{
		var result = CliHostRunner.Run("dry-run-cmd", "--dry-run");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dry-run:true");
	}

	[Fact]
	public void NullableBool_no_dry_run_false()
	{
		var result = CliHostRunner.Run("dry-run-cmd", "--no-dry-run");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dry-run:false");
	}

	[Fact]
	public void NullableBool_no_flag_null()
	{
		var result = CliHostRunner.Run("dry-run-cmd");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dry-run:null");
	}

	[Fact]
	public void Int_count()
	{
		var result = CliHostRunner.Run("count-cmd", "--count", "42");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("count:42");
	}

	[Fact]
	public void FileInfo_echo()
	{
		var result = CliHostRunner.Run("file-cmd", "--file", "/tmp/test.txt");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("file:test.txt");
	}

	[Fact]
	public void DirectoryInfo_echo()
	{
		var result = CliHostRunner.Run("dir-cmd", "--dir", "/tmp/mydir");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dir:mydir");
	}

	[Fact]
	public void Uri_echo()
	{
		var result = CliHostRunner.Run("uri-cmd", "--uri", "https://example.com/path");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("uri:example.com");
	}

	[Fact]
	public void CustomParser_point()
	{
		var result = CliHostRunner.Run("point-cmd", "--point", "3,4");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("point:3,4");
	}

	[Fact]
	public void Anonymous_lambda_command()
	{
		var result = CliHostRunner.Run("lambda-cmd", "--msg", "hi");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("lambda:hi");
	}

	[Fact]
	public void Documented_named_handler_doc_lambda()
	{
		var result = CliHostRunner.Run("doc-lambda", "--line", "ping");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("doc-lambda:ping");
	}
}
