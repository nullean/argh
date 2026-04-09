using Microsoft.Extensions.Logging;
using Nullean.Argh.Middleware;

namespace HostedRoot;

internal sealed class HostedRootGlobalMiddleware : ICommandMiddleware
{
	private readonly ILogger<HostedRootGlobalMiddleware> _logger;

	public HostedRootGlobalMiddleware(ILogger<HostedRootGlobalMiddleware> logger) =>
		_logger = logger;

	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		_logger.LogInformation("HostedRoot middleware: {Path}", string.Join("/", context.CommandPath));
		await next(context);
	}
}
