using System.Text;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Nullean.Argh.Generator.Tests;

/// <summary>
/// A command parameter whose default is an enum member must compile whatever that member's value is: C# converts only
/// the literal <c>0</c> to an enum implicitly (https://github.com/nullean/argh/issues/75).
/// </summary>
public class EnumDefaultTests
{
	private static string Source(string parameter) =>
		$$"""
		using Nullean.Argh;
		using Nullean.Argh.Builder;

		namespace App
		{
			public enum OneBased { None = 1, A = 2 }

			public static class Handlers
			{
				public static void Run({{parameter}}) { }
			}

			public static class CliRegistration
			{
				public static void Configure(IArghRootBuilder app) => app.Map("run", Handlers.Run);
			}
		}
		""";

	private static List<Diagnostic> GeneratedCompilationErrors(string source)
	{
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
		return output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
	}

	[Test]
	[Arguments("OneBased level = OneBased.None")]
	[Arguments("OneBased level = OneBased.A")]
	[Arguments("OneBased? level = OneBased.A")]
	[Arguments("OneBased level = (OneBased)7")]
	public void Default_of_a_non_zero_enum_member_compiles(string parameter)
	{
		var errors = GeneratedCompilationErrors(Source(parameter));

		errors.Should().BeEmpty(string.Join(Environment.NewLine, errors));
	}
}
