using Nullean.Argh.Filters;

namespace Basic;

/// <summary>Runs around every command after routing (<c>UseFilter&lt;T&gt;()</c>). This is the Argh &quot;middleware&quot; hook.</summary>
internal sealed class GlobalExampleFilter : ICommandFilter
{
	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		Console.Error.WriteLine($"[basic:filter:global] path={string.Join("/", context.CommandPath)}");
		await next(context);
	}
}

/// <summary>Per-command filter; see <c>[FilterAttribute&lt;T&gt;]</c> on a handler.</summary>
internal sealed class PerCommandExampleFilter : ICommandFilter
{
	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		Console.Error.WriteLine("[basic:filter:per-command] before");
		await next(context);
		Console.Error.WriteLine("[basic:filter:per-command] after");
	}
}

/// <summary>Second global filter to demonstrate ordering: global filters run in registration order.</summary>
internal sealed class OrderingDemoFilter : ICommandFilter
{
	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		Console.Error.WriteLine("[basic:filter:ordering] enter");
		await next(context);
		Console.Error.WriteLine("[basic:filter:ordering] leave");
	}
}
