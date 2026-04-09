using System.Runtime.CompilerServices;
using Nullean.Argh.Builder;
using Nullean.Argh.Tests.Fixtures;

namespace Nullean.Argh.Tests;

internal static class CliRegistrationModule
{
	[ModuleInitializer]
	internal static void RegisterCommands()
	{
		IArghBuilder app = new ArghBuilder();
		app.UseFilter<TestsGlobalFilter>();
		app.GlobalOptions<TestGlobalCliOptions>();
		app.Add("hello", CliTestHandlers.Hello);
		app.Add("enum-cmd", CliTestHandlers.EnumCmd);
		app.Add("deploy", CliTestHandlers.Deploy);
		app.Add("tags", CliTestHandlers.Tags);
		app.Add("dry-run-cmd", CliTestHandlers.DryRunCmd);
		app.Add("count-cmd", CliTestHandlers.CountCmd);
		app.Add("file-cmd", CliTestHandlers.FileCmd);
		app.Add("dir-cmd", CliTestHandlers.DirCmd);
		app.Add("uri-cmd", CliTestHandlers.UriCmd);
		app.Add("point-cmd", CliTestHandlers.PointCmd);
		app.Add("doc-lambda", DocLambdaEcho);
		// Anonymous lambdas have no XML docs; use a named handler (e.g. DocLambdaEcho) for help text.
		app.Add("lambda-cmd", (string msg) => Console.Out.WriteLine($"lambda:{msg}"));
		app.Add<DiProbeCommands>();
		app.AddNamespace("storage", g =>
		{
			g.CommandNamespaceOptions<TestStorageCommandNamespaceOptions>();
			g.Add<StorageCliCommands>();
		});
	}

	/// <summary>Documented handler for lambda-style <c>Add</c> (XML appears in help).</summary>
	/// <param name="line">-l,--line, Text line to echo.</param>
	/// <example>doc-lambda --line hi</example>
	internal static void DocLambdaEcho(string line) =>
		Console.Out.WriteLine($"doc-lambda:{line}");
}

