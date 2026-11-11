using System.Text;
using ProcNet;

namespace Nullean.Make;

/// <summary>
/// Runs commands from target bodies. Bring it into scope with <c>using static Nullean.Make.Shell;</c>:
/// <code>
/// Clean => app.Target(() => Exec("dotnet clean")),
/// </code>
/// A command inherits the console, and a non-zero exit code fails the target.
/// </summary>
public static class Shell
{
	/// <summary>
	/// Runs a command line such as <c>dotnet build -c Release</c>. Arguments split on whitespace; double quotes
	/// group an argument that contains spaces (<c>"My Project.csproj"</c>), and <c>\"</c> is a literal quote.
	/// </summary>
	/// <exception cref="ArgumentException">The command line is empty.</exception>
	public static void Exec(string commandLine)
	{
		var parts = Split(commandLine);
		if (parts.Count == 0)
			throw new ArgumentException("The command line is empty.", nameof(commandLine));
		Proc.Exec(parts[0], parts.Skip(1).ToArray());
	}

	/// <summary>Runs <paramref name="binary"/> with each argument passed as is, with no splitting or quoting.</summary>
	public static void Exec(string binary, params string[] arguments) => Proc.Exec(binary, arguments);

	/// <summary>Splits a command line the way <see cref="Exec(string)"/> does.</summary>
	public static IReadOnlyList<string> Split(string commandLine)
	{
		var parts = new List<string>();
		var current = new StringBuilder();
		var inQuotes = false;
		var hasToken = false;
		for (var i = 0; i < commandLine.Length; i++)
		{
			var c = commandLine[i];
			if (c == '\\' && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
			{
				current.Append('"');
				hasToken = true;
				i++;
			}
			else if (c == '"')
			{
				inQuotes = !inQuotes;
				hasToken = true;
			}
			else if (char.IsWhiteSpace(c) && !inQuotes)
			{
				if (hasToken)
				{
					parts.Add(current.ToString());
					current.Clear();
					hasToken = false;
				}
			}
			else
			{
				current.Append(c);
				hasToken = true;
			}
		}
		if (inQuotes)
			throw new ArgumentException($"Unclosed quote in command line: {commandLine}", nameof(commandLine));
		if (hasToken)
			parts.Add(current.ToString());
		return parts;
	}
}
