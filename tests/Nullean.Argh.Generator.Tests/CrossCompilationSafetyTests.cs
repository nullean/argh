using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Nullean.Argh.Generator.Tests;

/// <summary>
/// Regression coverage for a crash observed in solution-wide IDE builds (e.g. Rider/VS opening a .sln/.slnx):
/// when an options type passed to <c>UseGlobalOptions&lt;T&gt;</c>/<c>UseNamespaceOptions&lt;T&gt;</c> is
/// declared in a *different* project than the invocation, IDE workspaces represent that ProjectReference as a
/// live <see cref="CompilationReference"/> (not a metadata DLL). The type's declaring syntax then belongs to
/// the referenced project's <see cref="Compilation"/>, not the one the generator is analyzing — calling
/// <c>compilation.GetSemanticModel(thatSyntaxTree)</c> throws <see cref="ArgumentException"/>
/// ("SyntaxTree is not part of the compilation"), which aborts the whole generator (CS8785) for that project.
/// `dotnet build` from the command line doesn't hit this because MSBuild passes ProjectReferences as compiled
/// metadata, not live compilations — only IDE-hosted workspace builds do.
/// </summary>
public class CrossCompilationSafetyTests
{
	private static ImmutableArray<MetadataReference> BuildBaseReferences()
	{
		var tpaPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator);

		var builder = ImmutableArray.CreateBuilder<MetadataReference>();
		foreach (var path in tpaPaths)
			builder.Add(MetadataReference.CreateFromFile(path));

		builder.Add(MetadataReference.CreateFromFile(typeof(Nullean.Argh.Builder.IArghBuilder).Assembly.Location));
		return builder.ToImmutable();
	}

	private static CSharpCompilation CreateCompilation(string assemblyName, string source, ImmutableArray<MetadataReference> references)
	{
		var tree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), path: $"{assemblyName}.cs");
		return CSharpCompilation.Create(
			assemblyName,
			new[] { tree },
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false));
	}

	[Fact]
	public void Generator_does_not_crash_when_global_options_type_lives_in_a_referenced_compilation()
	{
		var baseReferences = BuildBaseReferences();

		const string sharedSource = """
			namespace Shared
			{
				public class SharedOptions
				{
					public int RetryCount { get; set; } = 5;
					public LogLevel Level { get; set; } = LogLevel.Info;
				}

				public enum LogLevel { Info, Warning, Error }
			}
			""";
		var sharedCompilation = CreateCompilation("SharedLib", sharedSource, baseReferences);
		AssertNoErrors(sharedCompilation);

		const string appSource = """
			using Nullean.Argh.Builder;
			using Shared;

			namespace App
			{
				public static class CliRegistration
				{
					public static void Configure(IArghBuilder builder)
					{
						builder.UseGlobalOptions<SharedOptions>();
					}
				}
			}
			""";
		// Mirrors how IDE workspaces (Rider/VS) resolve ProjectReferences when a solution is open:
		// a live CompilationReference to the referenced project, not a compiled metadata DLL.
		var appReferences = baseReferences.Add(sharedCompilation.ToMetadataReference());
		var appCompilation = CreateCompilation("App", appSource, appReferences);
		AssertNoErrors(appCompilation);

		var driver = CSharpGeneratorDriver.Create(new CliParserGenerator());
		driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(appCompilation, out _, out var diagnostics);

		var runResult = driver.GetRunResult();
		var generatorResult = Assert.Single(runResult.Results);
		Assert.Null(generatorResult.Exception);
		Assert.DoesNotContain(diagnostics, d => d.Id == "CS8785");
	}

	private static void AssertNoErrors(CSharpCompilation compilation)
	{
		var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
	}
}
