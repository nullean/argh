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

	/// <summary>Yellow label for the opt-in default handler in root/namespace help (typically <c>(default command)</c>; see <c>AddRootCommand</c> / <c>AddNamespaceRootCommand</c>).</summary>
	public static string DefaultCommandLabel(string text) => Placeholder(text);

	/// <summary>XML <c>summary</c> block in command help (below usage, above global options).</summary>
	public static string DocSummaryLine(string text) => Wrap(text, "\x1b[37m");

	/// <summary>XML <c>remarks</c> block in command help (below summary, above global options).</summary>
	public static string DocRemarksLine(string text) => Wrap(text, "\x1b[90m");

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

		string styledName = nameIsNamespace ? Accent(name) : Placeholder(name);
		int pad = System.Math.Max(0, nameColumnWidth - name.Length);
		string continuationIndent = new string(' ', 2 + nameColumnWidth + 2);

		if (string.IsNullOrWhiteSpace(summary))
		{
			Console.Out.WriteLine("  " + styledName + new string(' ', pad));
			return;
		}

		string s = summary!.Trim();
		int descStartChars = 2 + nameColumnWidth + 2;
		int maxDescWidth = System.Math.Max(12, HelpTerminalWidth - descStartChars);

		bool firstContent = true;
		foreach (string line in WrapSummaryForHelpList(s, maxDescWidth))
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
		string[] paragraphs = text.Replace("\r\n", "\n").Split('\n');
		for (int i = 0; i < paragraphs.Length; i++)
		{
			if (i > 0)
				yield return "";

			string p = paragraphs[i].Trim();
			if (p.Length == 0)
				continue;

			foreach (string w in WordWrapBlock(p, maxWidth))
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

		string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		var line = new StringBuilder();
		foreach (string word in words)
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
