using System.Text;

namespace Nullean.Argh;

/// <summary>
/// Command-line string splitting aligned with generated <c>Route(string)</c> / <c>ArghGenerated</c> behavior.
/// </summary>
/// <remarks>
/// For a string command line, arguments are split on whitespace; double quotes (<c>"</c>) group segments (minimal shell-like splitting).
/// </remarks>
public static class ArghCli
{
	/// <summary>
	/// Splits <paramref name="commandLine"/> on whitespace; characters inside a pair of double quotes are preserved as one token (quotes are not included in the token).
	/// </summary>
	public static string[] SplitCommandLine(string commandLine) => ParseArgs(commandLine);

	private static string[] ParseArgs(string commandLine)
	{
		if (string.IsNullOrWhiteSpace(commandLine))
			return Array.Empty<string>();

		var list = new List<string>();
		var sb = new StringBuilder();
		var inQuotes = false;
		for (var i = 0; i < commandLine.Length; i++)
		{
			var c = commandLine[i];
			if (c == '"')
			{
				inQuotes = !inQuotes;
				continue;
			}

			if (c == ' ' && !inQuotes)
			{
				if (sb.Length > 0)
				{
					list.Add(sb.ToString());
					sb.Clear();
				}

				continue;
			}

			sb.Append(c);
		}

		if (sb.Length > 0)
			list.Add(sb.ToString());

		return list.ToArray();
	}
}
