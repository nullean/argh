using System.Collections.Generic;
using System.Text;

namespace Nullean.Argh.Help;

/// <summary>
/// ANSI styling for CLI help output. Disabled when <c>NO_COLOR</c> is set or stdout is redirected (non-interactive).
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class CliHelpFormatting
{
	/// <summary>Returns true when ANSI escape sequences may be written.</summary>
	public static bool UseAnsiColors =>
		string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
		&& !Console.IsOutputRedirected;

	/// <summary>Bold section titles (e.g. Usage, Arguments).</summary>
	public static string Section(string text) => Wrap(text, "\x1b[1m");

	/// <summary>Accent for command names and keywords.</summary>
	public static string Accent(string text) => Wrap(text, "\x1b[36m");

	/// <summary>Placeholders such as <c>&lt;env&gt;</c> and type hints.</summary>
	public static string Placeholder(string text) => Wrap(text, "\x1b[33m");

	/// <summary>Yellow label for the opt-in default handler in root/namespace help (typically <c>(default command)</c>; see <c>MapRoot</c>).</summary>
	public static string DefaultCommandLabel(string text) => Placeholder(text);

	/// <summary>XML <c>summary</c> block in command help (below usage, above global options).</summary>
	public static string DocSummaryLine(string text) => Wrap(text, "\x1b[37m");

	/// <summary>XML <c>remarks</c> block in command help (below summary, above global options).</summary>
	public static string DocRemarksLine(string text) => Wrap(text, "\x1b[90m");

	/// <summary>Reset ANSI attributes (after inline spans).</summary>
	public static string ResetAnsi() => UseAnsiColors ? "\x1b[0m" : "";

	/// <summary>Inline code / <c> — bold.</summary>
	public static string InlineCode(string text) => Wrap(text, "\x1b[1m");

	/// <summary>Bold (<b>).</summary>
	public static string Bold(string text) => Wrap(text, "\x1b[1m");

	/// <summary>Italic (<i>).</summary>
	public static string Italic(string text) => Wrap(text, "\x1b[3m");

	/// <summary>Underline (<u>).</summary>
	public static string Underline(string text) => Wrap(text, "\x1b[4m");

	/// <summary><see cref="…"/> / <exception cref="…"/> member display — cyan accent.</summary>
	public static string RefCref(string crefShortName) => Accent(crefShortName);

	/// <summary><paramref name="…"/> / <typeparamref name="…"/> — yellow placeholder.</summary>
	public static string RefName(string name) => Placeholder(name);

	/// <summary><see langword="…"/> — bold like inline code.</summary>
	public static string Langword(string keyword) => InlineCode(keyword);

	/// <summary>Plain text run in XML &lt;remarks&gt; (dim gray).</summary>
	public static string DocRemarksPlainText(string text)
	{
		if (string.IsNullOrEmpty(text) || !UseAnsiColors)
			return text;
		return "\x1b[90m" + text + "\x1b[0m";
	}

	/// <summary>Plain text run in XML &lt;summary&gt; (white).</summary>
	public static string DocSummaryPlainText(string text)
	{
		if (string.IsNullOrEmpty(text) || !UseAnsiColors)
			return text;
		return "\x1b[37m" + text + "\x1b[0m";
	}

	/// <summary>Section title for &lt;example&gt; blocks in rendered XML remarks (same style as <see cref="Section"/>).</summary>
	public static string ExampleSectionTitle() => Section("Example:");

	/// <summary>Extra indent for &lt;code&gt; lines in XML doc help (after the block line indent).</summary>
	public const string XmlDocCodeLinePrefix = "  ";

	/// <summary>One line of a &lt;code&gt; block (prefix with <see cref="XmlDocCodeLinePrefix"/> in the renderer).</summary>
	public static string CodeBlockLine(string line) => line;

	/// <summary>&lt;paramref&gt; / &lt;typeparamref&gt; in rendered doc — magenta, then resume remark/summary base.</summary>
	public static string DocParamRef(string name, bool forRemarks) => DocStyledSpan(name, "\x1b[35m", forRemarks);

	/// <summary>&lt;see cref&gt; / exception cref in rendered doc — cyan, then resume base.</summary>
	public static string DocRefCref(string crefShortName, bool forRemarks) => DocStyledSpan(crefShortName, "\x1b[36m", forRemarks);

	/// <summary>&lt;c&gt; in rendered doc.</summary>
	public static string DocInlineCodeSpan(string text, bool forRemarks) => DocStyledSpan(text, "\x1b[1m", forRemarks);

	/// <summary>&lt;b&gt; in rendered doc.</summary>
	public static string DocBoldSpan(string text, bool forRemarks) => DocStyledSpan(text, "\x1b[1m", forRemarks);

	/// <summary>&lt;i&gt; in rendered doc.</summary>
	public static string DocItalicSpan(string text, bool forRemarks) => DocStyledSpan(text, "\x1b[3m", forRemarks);

	/// <summary>&lt;u&gt; in rendered doc.</summary>
	public static string DocUnderlineSpan(string text, bool forRemarks) => DocStyledSpan(text, "\x1b[4m", forRemarks);

	/// <summary>&lt;see langword&gt; in rendered doc.</summary>
	public static string DocLangwordSpan(string keyword, bool forRemarks) => DocStyledSpan(keyword, "\x1b[1m", forRemarks);

	private static string DocStyledSpan(string text, string open, bool forRemarks)
	{
		if (string.IsNullOrEmpty(text) || !UseAnsiColors)
			return text;
		var cont = forRemarks ? "\x1b[90m" : "\x1b[37m";
		return open + text + cont;
	}

	/// <summary>OSC 8 hyperlink; plain text with URL when colors disabled. After the link, resumes remark/summary base color when ANSI is on.</summary>
	public static string Osc8Hyperlink(string url, string visibleText, bool forRemarks)
	{
		if (string.IsNullOrEmpty(visibleText))
			return "";
		if (!UseAnsiColors)
			return string.IsNullOrEmpty(url) ? visibleText : $"{visibleText} ({url})";
		if (string.IsNullOrEmpty(url))
			return visibleText + (forRemarks ? "\x1b[90m" : "\x1b[37m");
		return "\x1b]8;;" + url + "\x1b\\" + visibleText + "\x1b]8;;\x1b\\" + (forRemarks ? "\x1b[90m" : "\x1b[37m");
	}

	/// <summary>Width used when wrapping list summaries; falls back to 80 when output is redirected.</summary>
	public static int HelpTerminalWidth =>
		Console.IsOutputRedirected ? 80 : System.Math.Max(40, Console.WindowWidth);

	/// <summary>
	/// Root / namespace overview: one row per name with optional summary. Namespace names use <see cref="Accent"/>; command names use <see cref="Placeholder"/> (yellow) to match the default-handler label. Summary uses <see cref="DocSummaryLine"/>; long text wraps with the same indent as the first summary column.
	/// </summary>
	public static void WriteHelpListNameAndDescription(bool nameIsNamespace, string name, string? summary, int nameColumnWidth)
	{
		if (string.IsNullOrEmpty(name))
			return;

		if (nameColumnWidth < name.Length)
			nameColumnWidth = name.Length;

		var styledName = nameIsNamespace ? Accent(name) : Placeholder(name);
		var pad = System.Math.Max(0, nameColumnWidth - name.Length);
		var continuationIndent = new string(' ', 2 + nameColumnWidth + 2);

		if (string.IsNullOrWhiteSpace(summary))
		{
			Console.Out.WriteLine("  " + styledName + new string(' ', pad));
			return;
		}

		var s = summary!.Trim();
		var descStartChars = 2 + nameColumnWidth + 2;
		var maxDescWidth = System.Math.Max(12, HelpTerminalWidth - descStartChars);

		var firstContent = true;
		foreach (var line in WrapSummaryForHelpList(s, maxDescWidth))
		{
			if (line.Length == 0)
			{
				Console.Out.WriteLine();
				continue;
			}

			if (firstContent)
			{
				Console.Out.WriteLine("  " + styledName + new string(' ', pad) + "  " + DocSummaryLine(line));
				firstContent = false;
			}
			else
				Console.Out.WriteLine(continuationIndent + DocSummaryLine(line));
		}
	}

	private static IEnumerable<string> WrapSummaryForHelpList(string text, int maxWidth)
	{
		var paragraphs = text.Replace("\r\n", "\n").Split('\n');
		for (var i = 0; i < paragraphs.Length; i++)
		{
			if (i > 0)
				yield return "";

			var p = paragraphs[i].Trim();
			if (p.Length == 0)
				continue;

			foreach (var w in WordWrapBlock(p, maxWidth))
				yield return w;
		}
	}

	private static IEnumerable<string> WordWrapBlock(string text, int maxWidth)
	{
		if (maxWidth < 1)
			maxWidth = 1;

		if (text.Length <= maxWidth)
		{
			yield return text;
			yield break;
		}

		var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		var line = new StringBuilder();
		foreach (var word in words)
		{
			if (word.Length > maxWidth)
			{
				if (line.Length > 0)
				{
					yield return line.ToString();
					line.Clear();
				}

				yield return word;
				continue;
			}

			if (line.Length == 0)
			{
				line.Append(word);
				continue;
			}

			if (line.Length + 1 + word.Length <= maxWidth)
				line.Append(' ').Append(word);
			else
			{
				yield return line.ToString();
				line.Clear().Append(word);
			}
		}

		if (line.Length > 0)
			yield return line.ToString();
	}

	private static string Wrap(string text, string open) =>
		string.IsNullOrEmpty(text) || !UseAnsiColors ? text : open + text + "\x1b[0m";
}
