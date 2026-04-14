namespace XmlDocShowcase;

/// <summary>
/// <para>Focused XML documentation fragments for CLI help—each subcommand highlights one kind of tag or block.</para>
/// </summary>
/// <remarks>
/// More information for the namespace
/// </remarks>
internal sealed class XmlDocSamples(XmlDocNamespaceOptions options)
{
	/// <summary>
	/// Summary-only showcase: <c>inline code</c>, <see cref="System.Environment"/>, <see href="https://learn.microsoft.com/dotnet/csharp/">C# language</see>,
	/// <see langword="null"/>, <paramref name="label"/>, plus <b>bold</b>, <i>italic</i>, <u>underline</u>.
	/// </summary>
	/// <remarks>
	/// <para>Remarks stay short here so the summary line carries the inline-tag demo.</para>
	/// </remarks>
	/// <param name="label">Short label printed to stdout (maps to <c>--label</c>).</param>
	public Task<int> Inline(string label)
	{
		Console.WriteLine($"docs:inline:{label}");
		return Task.FromResult(0);
	}

	/// <summary>One-line summary for the remarks demo.</summary>
	/// <remarks>
	/// <para>First paragraph with <c>para</c> and <paramref name="note"/>.</para>
	/// <para>Second paragraph introduces a list:</para>
	/// <list type="bullet">
	/// <item><description>Bullet with <see cref="System.String"/>.</description></item>
	/// <item><description>Another item.</description></item>
	/// <item><see cref="Example"/></item>
	/// </list>
	/// Line break test:<br/>text continues on the next visual line.
	/// </remarks>
	/// <param name="note">Optional note (maps to <c>--note</c>).</param>
	public Task<int> Remarks(string note)
	{
		Console.WriteLine($"docs:remarks:{note}");
		return Task.FromResult(0);
	}

	/// <summary>Minimal summary; the <c>example</c> block lives in remarks.</summary>
	/// <remarks>
	/// <example>
	/// <code>
	/// dotnet run --project examples/XmlDocShowcase -- docs example --command "build"
	/// </code>
	/// </example>
	/// </remarks>
	/// <param name="command">Sample command name to echo.</param>
	public Task<int> Example(string command)
	{
		Console.WriteLine($"docs:example:{command}");
		return Task.FromResult(0);
	}

	/// <summary>Cross-references between handlers in remarks.</summary>
	/// <remarks>
	/// <list type="bullet">
	/// <item><description>Summary-focused tags: <see cref="Inline"/>.</description></item>
	/// <item><description>Lists and paragraphs: <see cref="Remarks"/>.</description></item>
	/// </list>
	/// </remarks>
	/// <param name="topic">Arbitrary topic string for binding.</param>
	public Task<int> CrossRefs(string topic)
	{
		Console.WriteLine($"docs:cross-refs:{topic}");
		return Task.FromResult(0);
	}
}
