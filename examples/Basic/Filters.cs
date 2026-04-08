using Nullean.Argh.Filters;

namespace Basic;

/// <summary>Runs around every command after routing (see <c>UseFilter&lt;T&gt;()</c>).</summary>
internal sealed class GlobalExampleFilter : ICommandFilter
{
	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		Console.Error.WriteLine($"[filter:global] → {string.Join("/", context.CommandPath)}");
		await next(context);
	}
}

/// <summary>Example per-command filter (see <c>[FilterAttribute&lt;T&gt;]</c> on a handler).</summary>
internal sealed class PerCommandExampleFilter : ICommandFilter
{
	public async ValueTask InvokeAsync(CommandContext context, CommandFilterDelegate next)
	{
		Console.Error.WriteLine("[filter:hello] before handler");
		await next(context);
		Console.Error.WriteLine("[filter:hello] after handler");
	}
}
