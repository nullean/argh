using Nullean.Argh.Builder;
using Nullean.Argh.Middleware;

namespace Nullean.Argh;

public sealed partial class ArghApp
{
	IArghBuilder IArghBuilder.GlobalOptions<T>() where T : class
	{
		_ = GlobalOptions<T>();
		return this;
	}

	IArghBuilder IArghBuilder.Add(string name, Delegate handler)
	{
		_ = Add(name, handler);
		return this;
	}

	IArghBuilder IArghBuilder.Add<T>() where T : class
	{
		_ = Add<T>();
		return this;
	}

	IArghBuilder IArghBuilder.AddNamespace(string name, Action<IArghBuilder> configure)
	{
		_ = AddNamespace(name, configure);
		return this;
	}

	IArghBuilder IArghBuilder.CommandNamespaceOptions<T>() where T : class
	{
		_ = CommandNamespaceOptions<T>();
		return this;
	}

	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		_ = UseMiddleware(middleware);
		return this;
	}

	IArghBuilder IArghBuilder.UseMiddleware<TMiddleware>()
	{
		_ = UseMiddleware<TMiddleware>();
		return this;
	}

	Task<int> IArghBuilder.RunAsync(string[] args) => RunAsync(args);
}
