using Microsoft.Extensions.Logging;
using Nullean.Argh.Middleware;

namespace HostedRoot;

internal sealed class HostedRootGlobalMiddleware(ILogger<HostedRootGlobalMiddleware> logger) : ICommandMiddleware
{
	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		logger.LogInformation("HostedRoot middleware: {Path}", string.Join("/", context.CommandPath));
		await next(context);
	}
}
