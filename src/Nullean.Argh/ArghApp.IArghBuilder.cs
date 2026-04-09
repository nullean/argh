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

	IArghBuilder IArghBuilder.AddRootCommand(Delegate handler)
	{
		_ = AddRootCommand(handler);
		return this;
	}

	IArghBuilder IArghBuilder.AddNamespaceRootCommand(Delegate handler)
	{
		_ = AddNamespaceRootCommand(handler);
		return this;
	}

	IArghBuilder IArghBuilder.AddNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = AddNamespace(name, description, configure);
		return this;
	}

	IArghBuilder IArghBuilder.AddNamespace<T>(string name, Action<IArghBuilder> configure) where T : class
	{
		_ = AddNamespace<T>(name, configure);
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
