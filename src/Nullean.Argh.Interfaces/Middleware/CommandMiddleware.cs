namespace Nullean.Argh.Middleware;

/// <summary>
/// Per-invocation context for command execution and middleware. Populated by generated code when the middleware pipeline is wired up.
/// </summary>
/// <remarks>
/// Middleware does not run for root <c>--help</c>, <c>--version</c>, <c>__completion</c>, <c>__complete</c>, <c>__schema</c>, or when printing command help (<c>--help</c>/<c>-h</c>) before the handler runs.
/// </remarks>
public sealed class CommandContext
{
	/// <param name="commandPath">Segments from the app root to the matched command (e.g. group then command).</param>
	/// <param name="args">Raw arguments for this invocation; the exact slice is defined by the generator.</param>
	/// <param name="cancellationToken">Cancellation for this CLI run.</param>
	public CommandContext(string[] commandPath, string[] args, CancellationToken cancellationToken = default)
		: this(commandPath, args, cancellationToken, Array.Empty<CommandArgument>())
	{
	}

	/// <param name="commandPath">Segments from the app root to the matched command (e.g. group then command).</param>
	/// <param name="args">Raw arguments for this invocation; the exact slice is defined by the generator.</param>
	/// <param name="cancellationToken">Cancellation for this CLI run.</param>
	/// <param name="arguments">The bound values the handler is about to receive.</param>
	public CommandContext(string[] commandPath, string[] args, CancellationToken cancellationToken, IReadOnlyList<CommandArgument> arguments)
	{
		CommandPath = commandPath ?? throw new ArgumentNullException(nameof(commandPath));
		Args = args ?? throw new ArgumentNullException(nameof(args));
		CancellationToken = cancellationToken;
		Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
	}

	/// <summary>
	/// The bound option objects for this invocation: every <c>[AsParameters]</c> parameter and every global or namespace options object the handler receives.
	/// Plain flag and positional parameters are not listed. Lets middleware validate what was parsed instead of re-parsing <see cref="Args"/>.
	/// </summary>
	public IReadOnlyList<CommandArgument> Arguments { get; }

	/// <summary>
	/// Reports a validation failure the way the generated parser does: writes <c>Error: --flag: message</c> (or <c>Error: message</c> without a flag) to stderr and sets <see cref="ExitCode"/> to 2.
	/// Call <see cref="WriteUsageFooter"/> once after the last error, then do not call the next middleware.
	/// </summary>
	public void ReportError(string message, string? flag = null)
	{
		Console.Error.WriteLine(flag is null ? $"Error: {message}" : $"Error: --{flag}: {message}");
		ExitCode = 2;
	}

	/// <summary>
	/// Writes what the generated parser prints after a bad value: the help rows for <paramref name="flag"/> (when the command has that flag), then <c>Run '&lt;app&gt; &lt;command&gt; --help' for usage.</c>
	/// Set by the generated code; does nothing when the context was built by hand.
	/// </summary>
	public void WriteUsageFooter(string? flag = null) => UsageFooter?.Invoke(flag);

	/// <summary>Writes the usage footer for an optional flag. Assigned by generated code.</summary>
	public Action<string?>? UsageFooter { get; set; }

	/// <summary>Segments from the root to the matched command.</summary>
	public string[] CommandPath { get; }

	/// <summary>Raw command-line arguments for this invocation.</summary>
	public string[] Args { get; }

	/// <summary>Leaf command name, or <see cref="string.Empty"/> when <see cref="CommandPath"/> is empty.</summary>
	public string CommandName => CommandPath.Length == 0 ? string.Empty : CommandPath[CommandPath.Length - 1];

	/// <summary>Process exit code after the command and middleware complete; middleware may read or assign this value.</summary>
	public int ExitCode { get; set; }

	/// <summary>Cancellation token for this invocation.</summary>
	public CancellationToken CancellationToken { get; }
}

/// <summary>A bound option object handed to middleware through <see cref="CommandContext.Arguments"/>.</summary>
public sealed class CommandArgument
{
	/// <param name="name">The handler parameter name, or the options type name for global and namespace options.</param>
	/// <param name="value">The bound object.</param>
	/// <param name="memberFlags">CLR member name to CLI flag name (without dashes) for the object's members, or null when not known.</param>
	public CommandArgument(string name, object value, IReadOnlyDictionary<string, string>? memberFlags = null)
	{
		Name = name;
		Value = value;
		MemberFlags = memberFlags;
	}

	public string Name { get; }

	public object Value { get; }

	public IReadOnlyDictionary<string, string>? MemberFlags { get; }
}

/// <summary>
/// Represents the next stage in the command middleware pipeline (following the same pattern as <c>RequestDelegate</c>).
/// </summary>
/// <param name="context">The command context; pass through unchanged unless the middleware replaces invocation state.</param>
public delegate ValueTask CommandMiddlewareDelegate(CommandContext context);

/// <summary>
/// Middleware that runs after routing, around command execution.
/// </summary>
/// <remarks>
/// Middleware does not run for root <c>--help</c>, <c>--version</c>, <c>__completion</c>, <c>__complete</c>, <c>__schema</c>, or when printing command help (<c>--help</c>/<c>-h</c>) before the handler runs.
/// </remarks>
public interface ICommandMiddleware
{
	/// <summary>
	/// Invokes the middleware. Call <paramref name="next"/> with <paramref name="context"/> to continue the pipeline.
	/// </summary>
	ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next);
}
