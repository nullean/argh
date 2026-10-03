using Nullean.Argh.Middleware;

namespace Nullean.Argh.Tests.Fixtures;

internal sealed class TestsGlobalMiddleware : ICommandMiddleware
{
	public static int InvokeCount;
	public static IReadOnlyList<CommandArgument> LastArguments = [];
	public static bool ReportAfterNext;

	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		InvokeCount++;
		LastArguments = context.Arguments;
		if (ReportAfterNext)
		{
			context.ReportError("reported by middleware", "run-port");
			context.WriteUsageFooter("run-port");
			return;
		}
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
