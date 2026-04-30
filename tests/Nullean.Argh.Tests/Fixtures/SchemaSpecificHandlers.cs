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
