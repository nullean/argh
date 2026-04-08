using Nullean.Argh;

namespace Nullean.Argh.Tests.Fixtures;

internal sealed class TestsGlobalFilter : ICommandFilter
{
	public static int InvokeCount;

	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		InvokeCount++;
		await next(context);
	}
}

internal sealed class TestsPerCommandFilter : ICommandFilter
{
	public static int InvokeCount;

	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		InvokeCount++;
		await next(context);
	}
}
