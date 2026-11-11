using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Nullean.Argh.Generator.Tests;

/// <summary>
/// AGH0036: a union parameter's case names and case flags must be unique within the command.
/// The test sources declare unions in their lowered shape (a struct with <c>object? Value</c> and one-argument
/// constructors) so they parse with the Roslyn version the generator is built against.
/// </summary>
public class UnionFlagCollisionTests
{
	private static string Source(string cases, string unionCtors, string handler) => $$"""
		using Nullean.Argh;
		using Nullean.Argh.Builder;

		namespace App
		{
			{{cases}}

			public struct Format
			{
				public object? Value { get; }
				{{unionCtors}}
			}

			public static class Handlers
			{
				{{handler}}
			}

			public static class CliRegistration
			{
				public static void Configure(IArghRootBuilder app) => app.Map("convert", Handlers.Convert);
			}
		}
		""";

	private static ImmutableArray<Diagnostic> RunGenerator(string source)
	{
		var tpaPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
		var references = tpaPaths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
		references.Add(MetadataReference.CreateFromFile(typeof(Nullean.Argh.Builder.IArghBuilder).Assembly.Location));

		var tree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), path: "App.cs");
		var compilation = CSharpCompilation.Create("App", new[] { tree }, references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
		var sourceErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
		Assert.True(sourceErrors.Count == 0, string.Join(Environment.NewLine, sourceErrors));

		var driver = CSharpGeneratorDriver.Create(new CliParserGenerator());
		driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
		return diagnostics;
	}

	private static IEnumerable<Diagnostic> Agh0036(ImmutableArray<Diagnostic> diagnostics) =>
		diagnostics.Where(d => d.Id == "AGH0036");

	private const string JsonTableCtors = """
		public Format(Json value) => Value = value;
		public Format(Table value) => Value = value;
		""";

	[Fact]
	public void Flag_mode_case_flag_colliding_with_another_flag_is_reported()
	{
		var diagnostics = RunGenerator(Source(
			"public record class Json(bool Pretty = false); public record class Table(bool Pretty = false);",
			JsonTableCtors,
			"public static void Convert(Format format, bool jsonPretty = false) { }"));

		var d = Assert.Single(Agh0036(diagnostics));
		Assert.Equal(DiagnosticSeverity.Error, d.Severity);
		Assert.Contains("--json-pretty", d.GetMessage());
	}

	[Fact]
	public void Flag_mode_case_flag_colliding_with_another_case_flag_is_reported()
	{
		// Json.PrettyPrint and JsonPretty.Print both map to --json-pretty-print.
		var diagnostics = RunGenerator(Source(
			"public record class Json(bool PrettyPrint = false); public record class JsonPretty(bool Print = false);",
			"""
			public Format(Json value) => Value = value;
			public Format(JsonPretty value) => Value = value;
			""",
			"public static void Convert(Format format) { }"));

		var d = Assert.Single(Agh0036(diagnostics));
		Assert.Contains("--json-pretty-print", d.GetMessage());
	}

	[Fact]
	public void Argument_mode_shared_prop_with_different_types_is_reported()
	{
		var diagnostics = RunGenerator(Source(
			"public record class Json(int Pretty = 0); public record class Table(bool Pretty = false);",
			JsonTableCtors,
			"public static void Convert([Argument] Format format) { }"));

		var d = Assert.Single(Agh0036(diagnostics));
		Assert.Contains("--pretty", d.GetMessage());
	}

	[Fact]
	public void Duplicate_case_cli_names_are_reported()
	{
		var diagnostics = RunGenerator(Source(
			"public record class Json; public static class V2 { public record class Json; }",
			"""
			public Format(Json value) => Value = value;
			public Format(V2.Json value) => Value = value;
			""",
			"public static void Convert(Format format) { }"));

		var d = Assert.Single(Agh0036(diagnostics));
		Assert.Contains("case 'json'", d.GetMessage());
	}

	[Fact]
	public void Argument_mode_shared_prop_with_the_same_type_is_allowed()
	{
		var diagnostics = RunGenerator(Source(
			"public record class Json(bool Pretty = false, int Indent = 2); public record class Table(bool Pretty = false);",
			JsonTableCtors,
			"public static void Convert([Argument] Format format) { }"));

		Assert.Empty(Agh0036(diagnostics));
	}

	[Fact]
	public void Flag_mode_prefixed_case_flags_do_not_collide()
	{
		var diagnostics = RunGenerator(Source(
			"public record class Json(bool Pretty = false); public record class Table(bool Pretty = false);",
			JsonTableCtors,
			"public static void Convert(Format format, bool verbose = false) { }"));

		Assert.Empty(Agh0036(diagnostics));
	}

	[Theory]
	[InlineData("System.DateTime Since = default", "'Json.Since' (DateTime)")]
	[InlineData("int? Indent = null", "'Json.Indent' (int?)")]
	[InlineData("bool? Pretty = null", "'Json.Pretty' (bool?)")]
	[InlineData("string[]? Tags = null", "'Json.Tags' (string[]?)")]
	public void Unsupported_case_property_type_is_reported(string ctorParam, string expected)
	{
		var diagnostics = RunGenerator(Source(
			$"public record class Json({ctorParam}); public record class Table(bool Pretty = false);",
			JsonTableCtors,
			"public static void Convert(Format format) { }"));

		var d = Assert.Single(diagnostics, x => x.Id == "AGH0037");
		Assert.Equal(DiagnosticSeverity.Error, d.Severity);
		Assert.Contains(expected, d.GetMessage());
	}

	[Fact]
	public void Supported_case_property_types_are_not_reported()
	{
		var diagnostics = RunGenerator(Source(
			"public enum Level { Low, High = 3 } public record class Json(string Name = \"x\", int A = 1, long B = 2, float C = 1.5f, double D = 2.5, decimal E = 3.5m, Level L = Level.High, bool P = true); public record class Table(bool Pretty = false);",
			JsonTableCtors,
			"public static void Convert(Format format) { }"));

		Assert.DoesNotContain(diagnostics, x => x.Id == "AGH0037");
	}
}
