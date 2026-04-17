using Nullean.Argh;
using Nullean.Argh.Middleware;
using Nullean.Argh.Runtime;

namespace Nullean.Argh.Builder;

/// <summary>
/// Default <see cref="IArghBuilder"/> implementation; holds an <see cref="ArghApp"/> for source generator analysis.
/// </summary>
public sealed class ArghBuilder : IArghBuilder
{
	private readonly ArghApp _app;

	public ArghBuilder() : this(new ArghApp())
	{
	}

	internal ArghBuilder(ArghApp app) =>
		_app = app ?? throw new ArgumentNullException(nameof(app));

	internal ArghApp App => _app;

	public IArghBuilder UseGlobalOptions<T>() where T : class
	{
		_ = _app.UseGlobalOptions<T>();
		return this;
	}

	public IArghBuilder Map(string name, Delegate handler)
	{
		_ = _app.Map(name, handler);
		return this;
	}

	public IArghBuilder Map<T>() where T : class
	{
		_ = _app.Map<T>();
		return this;
	}

	public IArghBuilder MapRoot(Delegate handler)
	{
		_ = _app.MapRoot(handler);
		return this;
	}

	public IArghBuilder MapNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = _app.MapNamespace(name, description, configure);
		return this;
	}

	public IArghBuilder MapNamespace<T>(string name) where T : class
	{
		_ = _app.MapNamespace<T>(name);
		return this;
	}

	public IArghBuilder MapNamespace<T>(string name, Action<IArghBuilder> configure) where T : class
	{
		_ = _app.MapNamespace<T>(name, configure);
		return this;
	}

	public IArghBuilder MapNamespace<T>(string name, Action<IArghNamespaceBuilder> configure) where T : class
	{
		_ = _app.MapNamespace<T>(name, configure);
		return this;
	}

	public IArghBuilder MapNamespace<T>(Action<IArghNamespaceBuilder> configure) where T : class
	{
		_ = _app.MapNamespace<T>(configure);
		return this;
	}

	public IArghBuilder UseNamespaceOptions<T>() where T : class
	{
		_ = _app.UseNamespaceOptions<T>();
		return this;
	}

	public IArghBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		_ = _app.UseMiddleware(middleware);
		return this;
	}

	public IArghBuilder UseMiddleware<TMiddleware>() where TMiddleware : ICommandMiddleware
	{
		_ = _app.UseMiddleware<TMiddleware>();
		return this;
	}

	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);
}
