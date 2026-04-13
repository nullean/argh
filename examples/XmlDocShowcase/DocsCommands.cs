namespace XmlDocShowcase;

internal static class DocsCommands
{
	/// <summary>
	/// Demonstrates <c>inline code</c>, <see cref="System.Environment"/>, <see href="https://learn.microsoft.com/dotnet/csharp/">C# docs</see>,
	/// <see langword="null"/>, <paramref name="name"/>, and <b>bold</b> / <i>italic</i> / <u>underline</u>.
	/// </summary>
	/// <remarks>
	/// <para>First paragraph with <c>para</c>. <paramref name="name"/></para>
	/// <para>The second paragraph with a list:</para>
	/// <list type="bullet">
	/// <item><description>Item one</description></item>
	/// <item><description>Item two with <see cref="System.String"/></description></item>
	/// <item><see cref="Demo"/></item>
	/// </list>
	/// Line break test:<br/>next segment.
	/// <example>
	/// <code>
	/// dotnet run --project examples/XmlDocShowcase -- demo --name World
	/// </code>
	/// </example>
	/// </remarks>
	/// <param name="name">Greeting target name.</param>
	public static Task<int> Demo(string name)
	{
		Console.WriteLine($"Hello, {name}");
		return Task.FromResult(0);
	}

	public static Task<int> Demo2(string name, string other)
	{
		Console.WriteLine($"{nameof(Demo2)}, {name}, {other}");
		return Task.FromResult(0);
	}

	public static Task<int> Demo3(string name, string other)
	{
		Console.WriteLine($"{nameof(Demo3)}, {name}, {other}");
		return Task.FromResult(0);
	}
}
