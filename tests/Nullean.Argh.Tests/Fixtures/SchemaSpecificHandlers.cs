using Nullean.Argh;

namespace Nullean.Argh.Tests.Fixtures;

internal static class SchemaSpecificHandlers
{
	/// <summary>Schema test: command with a default value on --level.</summary>
	/// <param name="level">Verbosity level.</param>
	[NoOptionsInjection]
	public static void SchemaDefaultValue(int level = 3) =>
		Console.Out.WriteLine($"level:{level}");

	/// <summary>Schema test: command with a separator-based collection on --ids.</summary>
	/// <param name="ids">-i, Comma-separated IDs.</param>
	[NoOptionsInjection]
	public static void SchemaSeparatorList(
		[CollectionSyntax(Separator = ",")] List<int> ids) =>
		Console.Out.WriteLine("ids:" + string.Join(",", ids));

	/// <summary>Schema test: command with a hidden parameter.</summary>
	/// <param name="name">Visible name flag.</param>
	/// <param name="internalId">Internal ID (hidden).</param>
	[NoOptionsInjection]
	public static void SchemaHiddenParam(string name, [Hidden] string? internalId = null) =>
		Console.Out.WriteLine($"name:{name}");
}

/// <summary>Schema test: hidden command.</summary>
internal sealed class SchemaHiddenCommands
{
	/// <summary>A command that is visible.</summary>
	[NoOptionsInjection]
	public static void VisibleCmd() => Console.Out.WriteLine("visible");

	/// <summary>A command that is hidden from help and autocomplete.</summary>
	[Hidden]
	[NoOptionsInjection]
	public static void HiddenCmd() => Console.Out.WriteLine("hidden");
}

/// <summary>Schema test: deprecated commands and parameters.</summary>
internal static partial class SchemaDeprecatedHandlers
{
	/// <summary>A command that is deprecated without a message.</summary>
	[Obsolete]
	[NoOptionsInjection]
	public static void SchemaDeprecatedSimple() => Console.Out.WriteLine("deprecated-simple");

	/// <summary>A command that is deprecated with a migration message.</summary>
	[Obsolete("Use schema-deprecated-replacement instead.")]
	[NoOptionsInjection]
	public static void SchemaDeprecatedWithMessage() => Console.Out.WriteLine("deprecated-message");

}

/// <summary>Schema test: a DTO with a deprecated property, used via [AsParameters].</summary>
internal sealed class SchemaDeprecatedParamArgs
{
	/// <summary>The new flag to use.</summary>
	public string Name { get; init; } = "";

	/// <summary>Deprecated alias for name.</summary>
	[Obsolete("Use --name instead.")]
	public string? OldName { get; init; }
}

internal static partial class SchemaDeprecatedHandlers
{
	/// <summary>A command with a deprecated parameter via [AsParameters].</summary>
	[NoOptionsInjection]
	public static void SchemaDeprecatedParam([AsParameters] SchemaDeprecatedParamArgs args) =>
		Console.Out.WriteLine($"name:{args.Name ?? args.OldName}");
}

/// <summary>Schema test: intent annotation on a destructive command.</summary>
internal static class SchemaIntentHandlers
{
	/// <summary>Deletes all resources permanently.</summary>
	[CommandIntent(CommandIntentFlags.Destructive | CommandIntentFlags.RequiresConfirmation, Scope = CommandScope.Global)]
	[NoOptionsInjection]
	public static void SchemaIntentDestructive(
		[ConfirmationSkip] bool yes = false) =>
		Console.Out.WriteLine("deleted");

	/// <summary>Lists resources safely.</summary>
	[CommandIntent(CommandIntentFlags.Idempotent)]
	[NoOptionsInjection]
	public static void SchemaIntentRead(
		[DryRun] bool dryRun = false) =>
		Console.Out.WriteLine("list");
}

/// <summary>Schema test: output formats on a command.</summary>
internal static class SchemaOutputHandlers
{
	/// <summary>Reports status in multiple formats.</summary>
	[CommandOutput("json", "table", "csv", FormatFlag = "--output")]
	[NoOptionsInjection]
	public static void SchemaOutputFormats(string? output = null) =>
		Console.Out.WriteLine($"format:{output}");
}
