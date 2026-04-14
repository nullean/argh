namespace XmlDocShowcase;

internal static class DocsCommands
{
	/// <summary>
	/// Prints a one-line greeting using <c>stdout</c>, <see cref="System.Environment"/>,
	/// <see href="https://learn.microsoft.com/dotnet/csharp/">C# docs</see>, <see langword="null"/> notes,
	/// <paramref name="to"/>, and <b>bold</b> / <i>italic</i> / <u>underline</u>.
	/// </summary>
	/// <remarks>
	/// <para>Related workflows:</para>
	/// <list type="bullet">
	/// <item><description><paramref name="to"/> is a parameter</description></item>
	/// <item><see cref="Ping"/></item>
	/// </list>
	/// More detail on the next line:<br/>continues here without a new paragraph.
	/// <example>
	/// <code>
	/// dotnet run --project examples/XmlDocShowcase -- welcome --to World
	/// </code>
	/// </example>
	/// </remarks>
	/// <param name="globals">Injected global CLI options.</param>
	/// <param name="to">Display name or team to address (maps to <c>--to</c>).</param>
	public static Task<int> Welcome(XmlDocShowcaseGlobalOptions globals, string to)
	{
		if (globals.Verbose) Console.Error.WriteLine($"[verbose] Welcome handler, to={to}");
		Console.WriteLine($"Hello, {to}");
		return Task.FromResult(0);
	}

	/// <summary>Lightweight connectivity check (two string flags).</summary>
	public static Task<int> Ping(XmlDocShowcaseGlobalOptions globals, string environment, string region)
	{
		if (globals.Verbose) Console.Error.WriteLine($"[verbose] Ping handler, env={environment} region={region}");
		Console.WriteLine($"{nameof(Ping)}, {environment}, {region}");
		return Task.FromResult(0);
	}
}
