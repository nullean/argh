using System.Text;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Nullean.Argh.Generator.Tests;

/// <summary>
/// <c>MapTransient&lt;T&gt;</c>, <c>MapScoped&lt;T&gt;</c> and <c>MapSingleton&lt;T&gt;</c> on the hosting builder register
/// commands just like <c>Map&lt;T&gt;</c> (https://github.com/nullean/argh/issues/127).
/// </summary>
public class HostingMapLifetimeTests
{
	// Stand-in for the hosting builder's methods: the generator keys on the method name, not on the Hosting assembly.
	private static string Source(string call) =>
		$$"""
		using Nullean.Argh;
		using Nullean.Argh.Builder;

		namespace App
		{
			public class Commands
			{
				public void Frobnicate() { }
			}

			public static class HostingLike
			{
				public static IArghBuilder MapTransient<T>(this IArghBuilder b) where T : class => b.Map<T>();
				public static IArghBuilder MapScoped<T>(this IArghBuilder b) where T : class => b.Map<T>();
				public static IArghBuilder MapSingleton<T>(this IArghBuilder b) where T : class => b.Map<T>();
			}

			public static class CliRegistration
			{
				public static void Configure(IArghRootBuilder app) => app.{{call}}<Commands>();
			}
		}
		""";

	[Test]
	[Arguments("Map")]
	[Arguments("MapTransient")]
	[Arguments("MapScoped")]
	[Arguments("MapSingleton")]
	public void Command_type_is_registered(string call)
	{
		var tpaPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
		var references = tpaPaths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
		references.Add(MetadataReference.CreateFromFile(typeof(Nullean.Argh.Builder.IArghBuilder).Assembly.Location));
		references.Add(MetadataReference.CreateFromFile(typeof(Nullean.Argh.ArghApp).Assembly.Location));

		var tree = CSharpSyntaxTree.ParseText(SourceText.From(Source(call), Encoding.UTF8), path: "App.cs");
		var compilation = CSharpCompilation.Create(
			"App",
			[tree],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
		);

		var driver = CSharpGeneratorDriver.Create(new CliParserGenerator());
		var result = driver.RunGenerators(compilation).GetRunResult();
		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

		generated.Should().Contain("frobnicate");
	}
}
