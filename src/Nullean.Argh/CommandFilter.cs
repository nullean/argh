namespace Nullean.Argh;

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
