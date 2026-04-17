namespace Nullean.Argh.Help;

/// <summary>
/// Parses the <c>__complete</c> argv protocol: <c>app __complete &lt;shell&gt; -- [words...]</c>.
/// Words are the CLI tokens the shell would pass to the application, excluding the executable name
/// (same convention as normal invocation: first word is the first subcommand or flag).
/// </summary>
public static class CompletionProtocol
{
	/// <summary>
	/// Minimum length for a well-formed invocation: <c>__complete</c>, shell name, and <c>--</c>.
	/// </summary>
	public const int MinimumArgCount = 3;

	/// <summary>
	/// Returns <see langword="true"/> when <paramref name="args"/> begins with <c>__complete</c> (candidate completion).
	/// </summary>
	public static bool IsCompleteInvocation(string[] args) =>
		args is { Length: > 0 } && args[0] == "__complete";

	/// <summary>
	/// Returns <see langword="true"/> when <paramref name="args"/> begins with <c>__completion</c> (shell install script emission).
	/// </summary>
	public static bool IsCompletionScriptInvocation(string[] args) =>
		args is { Length: > 0 } && args[0] == "__completion";

	/// <summary>
	/// Returns <see langword="true"/> when <paramref name="args"/> is exactly <c>__schema</c> (JSON schema export).
	/// </summary>
	public static bool IsSchemaInvocation(string[] args) =>
		args is { Length: 1 } && args[0] == "__schema";

	/// <summary>
	/// Returns <see langword="true"/> when <paramref name="args"/> starts a meta-invocation (<c>__complete</c>, <c>__completion</c>, or <c>__schema</c>).
	/// Routing and middleware should ignore these (same as root <c>--help</c> / <c>--version</c> style bypasses).
	/// </summary>
	public static bool IsArghMetaCompletionInvocation(string[] args) =>
		args is { Length: > 0 } && (args[0] == "__complete" || args[0] == "__completion" || args[0] == "__schema");

	/// <summary>
	/// Parses <c>__completion bash|zsh|fish</c> for printing install scripts from <see cref="CompletionScriptTemplates"/>.
	/// </summary>
	public static bool TryParseCompletionScriptInvocation(string[] args, out CompletionShell shell)
	{
		shell = default;
		if (args is null || args.Length < 2)
			return false;
		if (args[0] != "__completion")
			return false;
		return TryParseShell(args[1], out shell);
	}

	/// <summary>
	/// Parses <c>__complete &lt;shell&gt; --</c> and returns words after the delimiter.
	/// </summary>
	/// <param name="args">Full process argv (includes <c>__complete</c>).</param>
	/// <param name="shell">Recognized shell enum value.</param>
	/// <param name="words">Slice of <paramref name="args"/> after <c>--</c>; may be empty.</param>
	/// <returns><see langword="false"/> if the prefix is not a valid complete invocation.</returns>
	public static bool TryParseCompleteInvocation(string[] args, out CompletionShell shell, out ReadOnlySpan<string> words)
	{
		shell = default;
		words = default;
		if (args is null || args.Length < MinimumArgCount)
			return false;
		if (args[0] != "__complete")
			return false;
		if (!TryParseShell(args[1], out shell))
			return false;
		if (args[2] != "--")
			return false;
		words = args.AsSpan(MinimumArgCount);
		return true;
	}

	/// <summary>
	/// Maps a shell name from argv to <see cref="CompletionShell"/>.
	/// </summary>
	public static bool TryParseShell(string shellName, out CompletionShell shell)
	{
		if (string.Equals(shellName, "bash", StringComparison.OrdinalIgnoreCase))
		{
			shell = CompletionShell.Bash;
			return true;
		}

		if (string.Equals(shellName, "zsh", StringComparison.OrdinalIgnoreCase))
		{
			shell = CompletionShell.Zsh;
			return true;
		}

		if (string.Equals(shellName, "fish", StringComparison.OrdinalIgnoreCase))
		{
			shell = CompletionShell.Fish;
			return true;
		}

		shell = default;
		return false;
	}
}
