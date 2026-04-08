namespace Nullean.Argh;

/// <summary>
/// ANSI styling for CLI help output. Disabled when <c>NO_COLOR</c> is set or stdout is redirected (non-interactive).
/// </summary>
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

	/// <summary>Muted secondary text.</summary>
	public static string Muted(string text) => Wrap(text, "\x1b[90m");

	private static string Wrap(string text, string open)
	{
		if (string.IsNullOrEmpty(text))
			return text;
		if (!UseAnsiColors)
			return text;
		return open + text + "\x1b[0m";
	}
}
