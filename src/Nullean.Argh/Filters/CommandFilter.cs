namespace Nullean.Argh.Filters;

/// <summary>
/// Per-invocation context for command execution and filters. Populated by generated code when the filter pipeline is wired up.
/// </summary>
public sealed class CommandContext
{
	/// <param name="commandPath">Segments from the app root to the matched command (e.g. group then command).</param>
	/// <param name="args">Raw arguments for this invocation; the exact slice is defined by the generator.</param>
	/// <param name="cancellationToken">Cancellation for this CLI run.</param>
	public CommandContext(string[] commandPath, string[] args, CancellationToken cancellationToken = default)
	{
		CommandPath = commandPath ?? throw new ArgumentNullException(nameof(commandPath));
		Args = args ?? throw new ArgumentNullException(nameof(args));
		CancellationToken = cancellationToken;
	}

	/// <summary>Segments from the root to the matched command.</summary>
	public string[] CommandPath { get; }

	/// <summary>Raw command-line arguments for this invocation.</summary>
	public string[] Args { get; }

	/// <summary>Leaf command name, or <see cref="string.Empty"/> when <see cref="CommandPath"/> is empty.</summary>
	public string CommandName => CommandPath.Length == 0 ? string.Empty : CommandPath[CommandPath.Length - 1];

	/// <summary>Process exit code after the command and filters complete; filters may read or assign this value.</summary>
	public int ExitCode { get; set; }

	/// <summary>Cancellation token for this invocation.</summary>
	public CancellationToken CancellationToken { get; }
}

/// <summary>
/// Represents the next stage in the command filter pipeline (following the same pattern as <c>RequestDelegate</c>).
/// </summary>
/// <param name="context">The command context; pass through unchanged unless the filter replaces invocation state.</param>
public delegate ValueTask CommandFilterDelegate(CommandContext context);

/// <summary>
/// A filter that runs after routing, around command execution.
/// </summary>
public interface ICommandFilter
{
	/// <summary>
	/// Invokes the filter. Call <paramref name="next"/> with <paramref name="context"/> to continue the pipeline.
	/// </summary>
	ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next);
}
