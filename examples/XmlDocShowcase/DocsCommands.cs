namespace XmlDocShowcase;

internal static class DocsCommands
{
	/// <summary>
	/// Prints a one-line greeting using <c>stdout</c>, <see cref="System.Environment"/>,
	/// <see href="https://learn.microsoft.com/dotnet/csharp/">C# docs</see>, <see langword="null"/> notes,
	/// <paramref name="to"/>, and <b>bold</b> / <i>italic</i> / <u>underline</u>.
	/// </summary>
	/// <remarks>
	/// <para>Use this in onboarding scripts or smoke tests before a deploy. <paramref name="to"/></para>
	/// <para>Related workflows:</para>
	/// <list type="bullet">
	/// <item><description>Run after configuring your profile locally.</description></item>
	/// <item><description>Compare with <see cref="System.String"/> for simple payloads.</description></item>
	/// <item><see cref="Ping"/></item>
	/// </list>
	/// More detail on the next line:<br/>continues here without a new paragraph.
	/// <example>
	/// <code>
	/// dotnet run --project examples/XmlDocShowcase -- welcome --to World
	/// </code>
	/// </example>
	/// </remarks>
	/// <param name="to">Display name or team to address (maps to <c>--to</c>).</param>
	public static Task<int> Welcome(string to)
	{
		Console.WriteLine($"Hello, {to}");
		return Task.FromResult(0);
	}

	/// <summary>Lightweight connectivity check (two string flags).</summary>
	public static Task<int> Ping(string environment, string region)
	{
		Console.WriteLine($"{nameof(Ping)}, {environment}, {region}");
		return Task.FromResult(0);
	}
}
