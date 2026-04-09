using System.Text.RegularExpressions;

namespace Nullean.Argh.IntegrationTests.Infrastructure;

internal static class ConsoleOutput
{
	private static readonly Regex s_ansi = new(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);

	/// <summary>Normalizes process capture for stable assertions: CRLF to LF, strip ANSI SGR sequences.</summary>
	internal static string Normalize(string text)
	{
		var t = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		return s_ansi.Replace(t, "");
	}
}
