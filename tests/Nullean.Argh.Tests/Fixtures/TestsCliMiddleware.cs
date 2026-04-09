using Nullean.Argh.Middleware;

namespace Nullean.Argh.Tests.Fixtures;

internal sealed class TestsGlobalMiddleware : ICommandMiddleware
{
	public static int InvokeCount;

	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		InvokeCount++;
		Console.Error.WriteLine("[tests:middleware:global]");
		await next(context);
	}
}

internal sealed class TestsPerCommandMiddleware : ICommandMiddleware
{
	public static int InvokeCount;

	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		InvokeCount++;
		Console.Error.WriteLine("[tests:middleware:per-command]");
		await next(context);
	}
}
