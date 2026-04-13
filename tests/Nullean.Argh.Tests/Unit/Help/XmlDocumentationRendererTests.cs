using System.IO;
using FluentAssertions;
using Nullean.Argh.Help;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Help;

public class XmlDocumentationRendererTests
{
	[Fact]
	public void Summary_collapses_comment_line_breaks_to_one_output_line()
	{
		const string inner = """
			Demonstrates <c>x</c>, <see cref="System.Environment"/>,
			<see langword="null"/>, <paramref name="n"/>.
			""";
		using var sw = new StringWriter();
		XmlDocumentationRenderer.WriteIndentedDoc(sw, "   ", inner, isRemarks: false);
		var lines = sw.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
		lines.Should().HaveCount(1);
		lines[0].Should().Contain("Demonstrates");
		lines[0].Should().Contain("null");
	}

	[Fact]
	public void Remarks_two_para_does_not_emit_runs_of_blank_lines()
	{
		const string inner = """
			<para>First.</para>
			<para>Second.</para>
			""";
		using var sw = new StringWriter();
		XmlDocumentationRenderer.WriteIndentedDoc(sw, "   ", inner, isRemarks: true);
		var text = sw.ToString().ReplaceLineEndings("\n");
		text.Should().NotContain("\n\n\n");
	}

	[Fact]
	public void Example_block_emits_bold_example_separator_and_plain_code_lines()
	{
		const string inner = "<example><code>dotnet run --help\n</code></example>";
		using var sw = new StringWriter();
		XmlDocumentationRenderer.WriteIndentedDoc(sw, "   ", inner, isRemarks: true);
		var s = sw.ToString();
		s.Should().Contain("--- example ---");
		s.Should().Contain("dotnet run --help");
		s.Should().NotContain("\x1b[7m");
		if (CliHelpFormatting.UseAnsiColors)
		{
			s.Should().Contain("\x1b[1mexample\x1b[0m");
		}
	}
}
