using System.ComponentModel;

namespace Nullean.Argh.Help;

/// <summary>
/// Shells for which static completion script templates are provided (<see cref="CompletionScriptTemplates"/>).
/// </summary>
public enum CompletionShell
{
	/// <summary>Bourne-again shell (<c>bash</c>).</summary>
	Bash = 0,

	/// <summary>Z shell (<c>zsh</c>).</summary>
	Zsh = 1,

	/// <summary>Friendly interactive shell (<c>fish</c>).</summary>
	Fish = 2,
}

/// <summary>
/// Shell completion script templates. Each template is a single string containing <c>{0}</c> placeholders for the application executable name. Prefer <c>template.Replace("{0}", appName)</c> (or escape literal braces as <c>{{</c>/<c>}}</c> if you use <see cref="string.Format(string, object?)"/>), because shell syntax includes braces that would confuse <c>string.Format</c>. The app prints these when invoked as <c>__completion bash|zsh|fish</c>; installed scripts call <c>__complete</c> for tab candidates (see <see cref="CompletionProtocol"/>).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CompletionScriptTemplates
{
	/// <summary>
	/// Bash completion script template. Replace <c>{0}</c> with the program name (e.g. <c>myapp</c>). The template assumes the CLI exposes a completion protocol such as <c>{0} __complete bash ...</c>.
	/// </summary>
	/// <returns>A format string with <c>{0}</c> for the executable name.</returns>
	public static string GetBash() =>
		"# Generated completion — replace {0} with your executable name.\n" +
		"# Passes all words after the executable (COMP_WORDS[1]..) so nested subcommands complete correctly.\n" +
		"_{0}_completion() {\n" +
		"  COMPREPLY=()\n" +
		"  mapfile -t COMPREPLY < <({0} __complete bash -- \"${COMP_WORDS[@]:1}\")\n" +
		"}\n" +
		"complete -o default -F _{0}_completion {0}\n";

	/// <summary>
	/// Zsh completion script template. Replace <c>{0}</c> with the program name. The template assumes <c>{0} __complete zsh ...</c>.
	/// </summary>
	/// <returns>A format string with <c>{0}</c> for the executable name.</returns>
	public static string GetZsh() =>
		"# Generated completion — replace {0} with your executable name.\n" +
		"# Passes argv after the command name (words[2]..) so nested subcommands complete correctly.\n" +
		"#compdef {0}\n" +
		"_{0}_completion() {\n" +
		"  local -a reply\n" +
		"  reply=($({0} __complete zsh -- \"${words[@]:2}\"))\n" +
		"  compadd -a reply\n" +
		"}\n" +
		"compdef _{0}_completion {0}\n";

	/// <summary>
	/// Fish completion script template. Replace <c>{0}</c> with the program name. The template assumes <c>{0} __complete fish ...</c>.
	/// </summary>
	/// <returns>A format string with <c>{0}</c> for the executable name.</returns>
	public static string GetFish() =>
		"# Generated completion — replace {0} with your executable name.\n" +
		"# Uses tokenized command line; drops the first token (program name). Requires fish 3.4+ for commandline -opc.\n" +
		"complete -c {0} -f -a \"({0} __complete fish -- (commandline -opc)[2..-1])\"\n";
}
