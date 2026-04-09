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
		app.UseMiddleware<TestsGlobalMiddleware>();
		app.GlobalOptions<TestGlobalCliOptions>();
		app.AddRootCommand(CliRootDefault);
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
		app.AddNamespace<StorageCliCommands>("storage", g =>
		{
			g.CommandNamespaceOptions<TestStorageCommandNamespaceOptions>();
			g.AddNamespaceRootCommand(StorageNamespaceRoot);
		});
	}

	/// <summary>Documented handler for lambda-style <c>Add</c> (XML appears in help).</summary>
	/// <param name="line">-l,--line, Text line to echo.</param>
	/// <example>doc-lambda --line hi</example>
	internal static void DocLambdaEcho(string line) =>
		Console.Out.WriteLine($"doc-lambda:{line}");

	/// <summary>Integration-test default when no subcommand is given at the app root.</summary>
	/// <remarks>Root default remarks for help layout tests.</remarks>
	internal static void CliRootDefault() =>
		Console.Out.WriteLine("marker:root-default");

	/// <summary>Integration-test default when only the <c>storage</c> namespace is selected.</summary>
	/// <remarks>Namespace default remarks for help layout tests.</remarks>
	internal static void StorageNamespaceRoot() =>
		Console.Out.WriteLine("marker:storage-ns-root");
}

