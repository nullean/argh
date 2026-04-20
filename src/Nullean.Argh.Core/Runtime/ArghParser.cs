namespace Nullean.Argh.Runtime;

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

/// <summary>
/// Routing helpers that delegate to source-generated code in the application assembly.
/// </summary>
/// <remarks>
/// <para>
/// Parameter binding for global options, group options, and <c>[AsParameters]</c> types is implemented as
/// <b>pregenerated C#</b> in the per-assembly CLI entry type (no reflection, AOT-safe), following the same idea as
/// <see href="https://github.com/Cysharp/ConsoleAppFramework/pull/237">ConsoleAppFramework PR #237</see>:
/// the generator emits parsers and object construction (<c>new T(...)</c>) for each registered shape.
/// </para>
/// <para>
/// There is no generic <c>Bind&lt;T&gt;()</c> that parses arbitrary <typeparamref name="T"/> at runtime; that would
/// require reflection or a non–AOT-safe registry. Tests should call generated entry points, run an integration host process, or invoke handlers/DTO constructors directly.
/// </para>
/// </remarks>
public static class ArghParser
{
	/// <summary>
	/// Routes argv to a registered command without invoking handlers. Uses the same routing rules as the generated CLI <c>RunAsync</c>, then delegates to <see cref="ArghRuntime.Route"/> (registered from generated code).
	/// </summary>
	/// <returns>The matched command path and remaining arguments, or <see langword="null"/> when no command is matched.</returns>
	public static RouteMatch? Route(string[] args)
	{
		if (args is null)
			throw new ArgumentNullException(nameof(args));
		return ArghRuntime.Route(args);
	}
}
