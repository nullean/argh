namespace Nullean.Argh;

/// <summary>
/// Result of routing a command line to a registered command without invoking handlers.
/// <see cref="CommandPath"/> uses <c>/</c> between group segments and the command (e.g. <c>storage/list</c>).
/// </summary>
public readonly struct RouteMatch(string commandPath, string[] remainingArgs)
{
	/// <summary>Registered command path, including nested groups, separated by <c>/</c>.</summary>
	public string CommandPath { get; } = commandPath ?? throw new ArgumentNullException(nameof(commandPath));

	/// <summary>Arguments after the matched command name (same slice the command handler receives).</summary>
	public string[] RemainingArgs { get; } = remainingArgs ?? throw new ArgumentNullException(nameof(remainingArgs));
}
