using AwesomeAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;

namespace Nullean.Argh.IntegrationTests.Commands;

public class CommandOutputTests
{
	[Test]
	public void Hello_ok_marker()
	{
		var result = CliHostRunner.Run("hello", "--name", "test");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("ok:test");
	}

	[Test]
	public void NullableBool_dry_run_true()
	{
		var result = CliHostRunner.Run("dry-run-cmd", "--dry-run");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dry-run:true");
	}

	[Test]
	public void NullableBool_no_dry_run_false()
	{
		var result = CliHostRunner.Run("dry-run-cmd", "--no-dry-run");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dry-run:false");
	}

	[Test]
	public void NullableBool_no_flag_null()
	{
		var result = CliHostRunner.Run("dry-run-cmd");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dry-run:null");
	}

	[Test]
	public void Int_count()
	{
		var result = CliHostRunner.Run("count-cmd", "--count", "42");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("count:42");
	}

	[Test]
	public void FileInfo_echo()
	{
		var result = CliHostRunner.Run("file-cmd", "--file", "/tmp/test.txt");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("file:test.txt");
	}

	[Test]
	public void DirectoryInfo_echo()
	{
		var result = CliHostRunner.Run("dir-cmd", "--dir", "/tmp/mydir");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("dir:mydir");
	}

	[Test]
	public void Uri_echo()
	{
		var result = CliHostRunner.Run("uri-cmd", "--uri", "https://example.com/path");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("uri:example.com");
	}

	[Test]
	public void Uri_help_plain_parameter_uses_uri_placeholder()
	{
		var result = CliHostRunner.Run("uri-cmd", "--help");
		result.ExitCode.Should().Be(0);
		ConsoleOutput.Normalize(CliHostRunner.StdoutText(result)).Should().Contain("--uri <uri>");
	}

	[Test]
	public void CustomParser_point()
	{
		var result = CliHostRunner.Run("point-cmd", "--point", "3,4");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("point:3,4");
	}

	[Test]
	public void CustomParser_property_on_as_parameters()
	{
		var result = CliHostRunner.Run("point-as-params-cmd", "--point", "7,8");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("point-as-params:7,8");
	}

	[Test]
	public void WholeCollection_ArgumentParser_on_IReadOnlySet_parameter()
	{
		var result = CliHostRunner.Run("tag-set-parser", "--tags", "c,a,b");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("tag-set-parser:a,b,c");
	}

	[Test]
	public void WholeCollection_ArgumentParser_on_nullable_IReadOnlySet_parameter_when_provided()
	{
		var result = CliHostRunner.Run("tag-set-parser-opt", "--tags", "x,y");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("tag-set-parser-opt:x,y");
	}

	[Test]
	public void WholeCollection_ArgumentParser_on_nullable_IReadOnlySet_parameter_when_absent()
	{
		var result = CliHostRunner.Run("tag-set-parser-opt");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("tag-set-parser-opt:null");
	}

	[Test]
	public void Anonymous_lambda_command()
	{
		var result = CliHostRunner.Run("lambda-cmd", "--msg", "hi");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("lambda:hi");
	}

	[Test]
	public void Documented_named_handler_doc_lambda()
	{
		var result = CliHostRunner.Run("doc-lambda", "--line", "ping");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("doc-lambda:ping");
	}

	[Test]
	[Arguments(0)]
	[Arguments(3)]
	[Arguments(42)]
	public void Synchronous_int_returning_method_handler_sets_exit_code(int code)
	{
		var result = CliHostRunner.Run("exit-code-cmd", "--code", code.ToString());
		result.ExitCode.Should().Be(code);
		CliHostRunner.StdoutText(result).Trim().Should().Be($"exit-code:{code}");
	}

	[Test]
	[Arguments(0)]
	[Arguments(7)]
	public void Synchronous_int_returning_lambda_handler_sets_exit_code(int code)
	{
		var result = CliHostRunner.Run("exit-code-lambda", "--code", code.ToString());
		result.ExitCode.Should().Be(code);
	}
}
