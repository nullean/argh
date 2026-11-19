using System.Text;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Nullean.Argh.Generator.Tests;

/// <summary>
/// <c>&lt;param&gt;</c> aliases and descriptions apply whether or not the doc comment has a <c>&lt;summary&gt;</c>
/// (https://github.com/nullean/argh/issues/74).
/// </summary>
public class ParamDocWithoutSummaryTests
{
	private static string Generated(string docComment)
	{
		var source =
			$$"""
			using Nullean.Argh;
			using Nullean.Argh.Builder;

			namespace App
			{
				public static class Handlers
				{
					{{docComment}}
					public static void Run(string input, string output) { }
				}

				public static class CliRegistration
				{
					public static void Configure(IArghRootBuilder app) => app.Map("run", Handlers.Run);
				}
			}
			""";

		var tpaPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
		var references = tpaPaths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
		references.Add(MetadataReference.CreateFromFile(typeof(Nullean.Argh.Builder.IArghBuilder).Assembly.Location));
		references.Add(MetadataReference.CreateFromFile(typeof(Nullean.Argh.ArghApp).Assembly.Location));

		var tree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), path: "App.cs");
		var compilation = CSharpCompilation.Create(
			"App",
			[tree],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
		);

		var driver = CSharpGeneratorDriver.Create(new CliParserGenerator());
		driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
		return string.Join("\n", output.SyntaxTrees.Select(t => t.ToString()));
	}

	private const string Params =
		"""
		/// <param name="input">-i, --in, Input path.</param>
		/// <param name="output">-o, --out, Output path.</param>
		""";

	[Test]
	public void Params_apply_without_a_summary()
	{
		var generated = Generated(Params);

		generated.Should().Contain("Input path.");
		generated.Should().Contain("\"--in\"");
	}

	[Test]
	public void Params_apply_with_a_summary()
	{
		var generated = Generated("/// <summary>Runs.</summary>\n" + Params);

		generated.Should().Contain("Input path.");
		generated.Should().Contain("\"--in\"");
	}
}
