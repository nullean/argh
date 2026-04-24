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
		app.UseGlobalOptions<TestGlobalCliOptions>();
		app.MapRoot(CliRootDefault);
		app.Map("hello", CliTestHandlers.Hello);
		app.Map("enum-cmd", CliTestHandlers.EnumCmd);
		app.Map("deploy", CliTestHandlers.Deploy);
		app.Map("as-params-with-ct", CliTestHandlers.AsParamsWithCt);
		app.Map("nullable-numeric-as-params", CliTestHandlers.NullableNumericAsParams);
		app.Map("multi-enum-as-params", CliTestHandlers.MultiEnumAsParams);
		app.Map("optional-uri-as-params", CliTestHandlers.OptionalUriAsParams);
		app.Map("prop-doc-as-params", CliTestHandlers.PropDocAsParams);
		app.Map("param-comment-record", CliTestHandlers.ParamCommentRecordCmd);
		app.Map("tags", CliTestHandlers.Tags);
		app.Map("brace-doc", CliTestHandlers.BraceDoc);
		app.Map("dry-run-cmd", CliTestHandlers.DryRunCmd);
		app.Map("count-cmd", CliTestHandlers.CountCmd);
		app.Map("file-cmd", CliTestHandlers.FileCmd);
		app.Map("dir-cmd", CliTestHandlers.DirCmd);
		app.Map("uri-cmd", CliTestHandlers.UriCmd);
		app.Map("temporal-cmd", CliTestHandlers.TemporalCmd);
		app.Map("point-cmd", CliTestHandlers.PointCmd);
		app.Map("doc-lambda", DocLambdaEcho);
		// Anonymous lambdas have no XML docs; use a named handler (e.g. DocLambdaEcho) for help text.
		app.Map("lambda-cmd", (string msg) => Console.Out.WriteLine($"lambda:{msg}"));
		app.Map("validate-range", ValidationCliHandlers.ValidateRange);
		app.Map("validate-length", ValidationCliHandlers.ValidateLength);
		app.Map("validate-regex", ValidationCliHandlers.ValidateRegex);
		app.Map("validate-allowed", ValidationCliHandlers.ValidateAllowed);
		app.Map("validate-email", ValidationCliHandlers.ValidateEmail);
		app.Map("validate-uri-scheme", ValidationCliHandlers.ValidateUriScheme);
		app.Map("validate-non-nullable-range", ValidationCliHandlers.ValidateNonNullableRange);
		app.Map("validate-dto", ValidationCliHandlers.ValidateDto);
		app.Map("validate-timespan-range", ValidationCliHandlers.ValidateTimeSpanRange);
		app.Map<DiProbeCommands>();
		app.Map<CommandNameOverrideCommands>();
		app.MapNamespace<StorageCliCommands>("storage", g =>
		{
			g.UseNamespaceOptions<TestStorageCommandNamespaceOptions>();
			g.MapRoot(StorageNamespaceRoot);
			g.MapNamespace<StorageCliCommands.BlobCommands>("blob");
		});
		// Expression-bodied configure lambdas: nested MapNamespace must stay under each parent (AGH0022); same nested segment under two parents is OK.
		app.MapNamespace("billing", "Billing commands", g => g.MapNamespace("tools", "Shared tools segment", h => h.Map("status", BillingToolsHandlers.Status)));
		app.MapNamespace("support", "Support commands", g => g.MapNamespace("tools", "Shared tools segment", h => h.Map("status", SupportToolsHandlers.Status)));
	}

	/// <summary>Documented handler for lambda-style <c>Map</c> (XML appears in help).</summary>
	/// <param name="g">Injected global CLI options.</param>
	/// <param name="line">-l,--line, Text line to echo.</param>
	/// <example>doc-lambda --line hi</example>
	internal static void DocLambdaEcho(TestGlobalCliOptions g, string line) =>
		Console.Out.WriteLine($"doc-lambda:{line}");

	/// <summary>Integration-test default when no subcommand is given at the app root.</summary>
	/// <remarks>Root default remarks for help layout tests.</remarks>
	internal static void CliRootDefault(TestGlobalCliOptions g) =>
		Console.Out.WriteLine("marker:root-default");

	/// <summary>Integration-test default when only the <c>storage</c> namespace is selected.</summary>
	/// <remarks>Namespace default remarks for help layout tests.</remarks>
	internal static void StorageNamespaceRoot(TestStorageCommandNamespaceOptions o) =>
		Console.Out.WriteLine("marker:storage-ns-root");
}
