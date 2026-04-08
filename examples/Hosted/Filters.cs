using Microsoft.Extensions.Logging;
using Nullean.Argh.Filters;

namespace Hosted;

internal sealed class HostedGlobalFilter : ICommandFilter
{
	private readonly ILogger<HostedGlobalFilter> _logger;

	public HostedGlobalFilter(ILogger<HostedGlobalFilter> logger) =>
		_logger = logger;

	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		_logger.LogInformation("Hosted global filter: {Path}", string.Join("/", context.CommandPath));
		await next(context);
	}
}

internal sealed class HostedPerCommandFilter : ICommandFilter
{
	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		Console.Error.WriteLine("[hosted filter] before hello");
		await next(context);
		Console.Error.WriteLine("[hosted filter] after hello");
	}
}
