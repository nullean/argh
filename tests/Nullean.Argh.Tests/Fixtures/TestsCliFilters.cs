using Nullean.Argh.Filters;

namespace Nullean.Argh.Tests.Fixtures;

internal sealed class TestsGlobalFilter : ICommandFilter
{
	public static int InvokeCount;

	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		InvokeCount++;
		Console.Error.WriteLine("[tests:filter:global]");
		await next(context);
	}
}

internal sealed class TestsPerCommandFilter : ICommandFilter
{
	public static int InvokeCount;

	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		InvokeCount++;
		Console.Error.WriteLine("[tests:filter:per-command]");
		await next(context);
	}
}
