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

	public IArghBuilder GlobalOptions<T>() where T : class
	{
		_ = _app.GlobalOptions<T>();
		return this;
	}

	public IArghBuilder Add(string name, Delegate handler)
	{
		_ = _app.Add(name, handler);
		return this;
	}

	public IArghBuilder Add<T>() where T : class
	{
		_ = _app.Add<T>();
		return this;
	}

	public IArghBuilder AddRootCommand(Delegate handler)
	{
		_ = _app.AddRootCommand(handler);
		return this;
	}

	public IArghBuilder AddNamespaceRootCommand(Delegate handler)
	{
		_ = _app.AddNamespaceRootCommand(handler);
		return this;
	}

	public IArghBuilder AddNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = _app.AddNamespace(name, description, configure);
		return this;
	}

	public IArghBuilder AddNamespace<T>(string name, Action<IArghBuilder> configure) where T : class
	{
		_ = _app.AddNamespace<T>(name, configure);
		return this;
	}

	public IArghBuilder CommandNamespaceOptions<T>() where T : class
	{
		_ = _app.CommandNamespaceOptions<T>();
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

	/// <inheritdoc cref="ArghApp.RunAsync"/>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);
}
