using Microsoft.Extensions.Logging;
using Nullean.Argh.Middleware;

namespace Hosted;

internal sealed class HostedGlobalMiddleware : ICommandMiddleware
{
	private readonly ILogger<HostedGlobalMiddleware> _logger;

	public HostedGlobalMiddleware(ILogger<HostedGlobalMiddleware> logger) =>
		_logger = logger;

	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		_logger.LogInformation("Hosted global middleware: {Path}", string.Join("/", context.CommandPath));
		await next(context);
	}
}

internal sealed class HostedOrderingDemoMiddleware : ICommandMiddleware
{
	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		Console.Error.WriteLine("[hosted:middleware:ordering] enter");
		await next(context);
		Console.Error.WriteLine("[hosted:middleware:ordering] leave");
	}
}

internal sealed class HostedPerCommandMiddleware : ICommandMiddleware
{
	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		Console.Error.WriteLine("[hosted middleware] before hello");
		await next(context);
		Console.Error.WriteLine("[hosted middleware] after hello");
	}
}
