using Nullean.Argh.Builder;
using Nullean.Argh.Middleware;

namespace Nullean.Argh;

public sealed partial class ArghApp
{
	IArghBuilder IArghBuilder.UseGlobalOptions<T>() where T : class
	{
		_ = UseGlobalOptions<T>();
		return this;
	}

	IArghBuilder IArghBuilder.Map(string name, Delegate handler)
	{
		_ = Map(name, handler);
		return this;
	}

	IArghBuilder IArghBuilder.Map<T>() where T : class
	{
		_ = Map<T>();
		return this;
	}

	IArghBuilder IArghBuilder.MapRoot(Delegate handler)
	{
		_ = MapRoot(handler);
		return this;
	}

	IArghBuilder IArghBuilder.MapNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = MapNamespace(name, description, configure);
		return this;
	}

	IArghBuilder IArghBuilder.MapNamespace<T>(string name) where T : class
	{
		_ = MapNamespace<T>(name);
		return this;
	}

	IArghBuilder IArghBuilder.MapNamespace<T>(string name, Action<IArghBuilder> configure) where T : class
	{
		_ = MapNamespace<T>(name, configure);
		return this;
	}

	IArghBuilder IArghBuilder.MapNamespace<T>(string name, Action<IArghNamespaceBuilder> configure) where T : class
	{
		_ = MapNamespace<T>(name, configure);
		return this;
	}

	IArghBuilder IArghBuilder.MapNamespace<T>(Action<IArghNamespaceBuilder> configure) where T : class
	{
		_ = MapNamespace<T>(configure);
		return this;
	}

	IArghBuilder IArghBuilder.UseNamespaceOptions<T>() where T : class
	{
		_ = UseNamespaceOptions<T>();
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
