using System.Text.Json;
using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Completions;

public class UnionSchemaAndCompletionTests
{
	private static JsonElement[] SchemaParameters(string command)
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var cmd = doc.RootElement.GetProperty("commands").EnumerateArray()
			.Single(c => c.GetProperty("name").GetString() == command);
		return cmd.GetProperty("parameters").EnumerateArray().Select(e => e.Clone()).ToArray();
	}

	private static JsonElement Param(JsonElement[] ps, string name) =>
		ps.Single(p => p.GetProperty("name").GetString() == name);

	[Fact]
	public void Schema_lists_the_selector_as_an_enum_of_case_names()
	{
		var selector = Param(SchemaParameters("union-shape"), "shape");
		selector.GetProperty("role").GetString().Should().Be("flag");
		selector.GetProperty("type").GetString().Should().Be("enum");
		selector.GetProperty("enumValues").EnumerateArray().Select(e => e.GetString()).Should().Equal("circle", "rect");
	}

	[Fact]
	public void Schema_lists_each_case_flag_with_its_type_and_default()
	{
		var ps = SchemaParameters("union-shape");
		var fill = Param(ps, "circle-fill");
		fill.GetProperty("type").GetString().Should().Be("enum");
		fill.GetProperty("defaultValue").GetString().Should().Be("none");
		fill.GetProperty("enumValues").EnumerateArray().Select(e => e.GetString()).Should().Equal("solid", "dash", "none");
		fill.GetProperty("summary").GetString().Should().Be("Applies when --shape is circle.");

		Param(ps, "rect-width").GetProperty("type").GetString().Should().Be("number");
		Param(ps, "rect-width").GetProperty("defaultValue").GetString().Should().Be("2.5");
		Param(ps, "rect-rounded").GetProperty("defaultValue").GetString().Should().Be("true");
	}

	[Fact]
	public void Schema_lists_argument_mode_selector_as_positional_and_shared_flags_once()
	{
		var ps = SchemaParameters("union-format-arg");
		Param(ps, "format").GetProperty("role").GetString().Should().Be("positional");
		ps.Count(p => p.GetProperty("name").GetString() == "pretty").Should().Be(1);
		Param(ps, "indent").GetProperty("type").GetString().Should().Be("integer");
	}

	[Fact]
	public void Completion_offers_case_flags()
	{
		var result = CliHostRunner.Run("__complete", "bash", "--", "union-shape", "--");
		result.ExitCode.Should().Be(0);
		var lines = CliHostRunner.StdoutText(result).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		lines.Should().Contain(new[] { "--shape", "--circle-radius", "--circle-fill", "--rect-width", "--rect-rounded", "--no-rect-rounded" });
	}

	[Fact]
	public void Completion_offers_bare_case_flags_in_argument_mode()
	{
		var result = CliHostRunner.Run("__complete", "bash", "--", "union-format-arg", "json", "--");
		result.ExitCode.Should().Be(0);
		var lines = CliHostRunner.StdoutText(result).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		lines.Should().Contain(new[] { "--pretty", "--indent" });
	}
}
