namespace Nullean.Argh;

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
