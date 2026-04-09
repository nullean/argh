using Nullean.Argh.Middleware;

namespace Basic;

/// <summary>Runs around every command after routing (<c>UseMiddleware&lt;T&gt;()</c>).</summary>
internal sealed class GlobalExampleMiddleware : ICommandMiddleware
{
	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		// Global middleware runs outermost; per-command middleware runs after global registrations.
		await next(context);
	}
}

/// <summary>Per-command middleware; see <c>[MiddlewareAttribute&lt;T&gt;]</c> on a handler.</summary>
internal sealed class PerCommandExampleMiddleware : ICommandMiddleware
{
	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		await next(context);
	}
}

/// <summary>Second global registration runs after <see cref="GlobalExampleMiddleware"/> (registration order).</summary>
internal sealed class OrderingDemoMiddleware : ICommandMiddleware
{
	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		await next(context);
	}
}
