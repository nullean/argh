namespace Nullean.Argh;

/// <summary>
/// Shell completion script templates. Each template is a single string containing <c>{0}</c> placeholders for the application executable name. Prefer <c>template.Replace("{0}", appName)</c> (or escape literal braces as <c>{{</c>/<c>}}</c> if you use <see cref="string.Format(string, object?)"/>), because shell syntax includes braces that would confuse <c>string.Format</c>. Wiring to a <c>--completions</c> flag can be added separately.
/// </summary>
public static class CompletionScriptTemplates
{
	/// <summary>
	/// Bash completion script template. Replace <c>{0}</c> with the program name (e.g. <c>myapp</c>). The template assumes the CLI exposes a completion protocol such as <c>{0} __complete bash ...</c>.
	/// </summary>
	/// <returns>A format string with <c>{0}</c> for the executable name.</returns>
	public static string GetBash() =>
		"# Generated completion — replace {0} with your executable name.\n" +
		"_{0}_completion() {\n" +
		"  local cur\n" +
		"  COMPREPLY=()\n" +
		"  cur=\"${COMP_WORDS[COMP_CWORD]}\"\n" +
		"  mapfile -t COMPREPLY < <({0} __complete bash -- \"${cur}\")\n" +
		"}\n" +
		"complete -o default -F _{0}_completion {0}\n";

	/// <summary>
	/// Zsh completion script template. Replace <c>{0}</c> with the program name. The template assumes <c>{0} __complete zsh ...</c>.
	/// </summary>
	/// <returns>A format string with <c>{0}</c> for the executable name.</returns>
	public static string GetZsh() =>
		"# Generated completion — replace {0} with your executable name.\n" +
		"#compdef {0}\n" +
		"_{0}_completion() {\n" +
		"  local -a reply\n" +
		"  reply=($({0} __complete zsh -- \"${words[CURRENT]}\"))\n" +
		"  compadd -a reply\n" +
		"}\n" +
		"compdef _{0}_completion {0}\n";

	/// <summary>
	/// Fish completion script template. Replace <c>{0}</c> with the program name. The template assumes <c>{0} __complete fish ...</c>.
	/// </summary>
	/// <returns>A format string with <c>{0}</c> for the executable name.</returns>
	public static string GetFish() =>
		"# Generated completion — replace {0} with your executable name.\n" +
		"complete -c {0} -f -a \"({0} __complete fish -- (commandline -ct))\"\n";

	/// <summary>
	/// Returns the template for the given <paramref name="shell"/>.
	/// </summary>
	/// <param name="shell">Target shell.</param>
	/// <returns>The same strings as <see cref="GetBash"/>, <see cref="GetZsh"/>, or <see cref="GetFish"/>.</returns>
	public static string Get(CompletionShell shell) =>
		shell switch
		{
			CompletionShell.Bash => GetBash(),
			CompletionShell.Zsh => GetZsh(),
			CompletionShell.Fish => GetFish(),
			_ => throw new ArgumentOutOfRangeException(nameof(shell)),
		};
}
