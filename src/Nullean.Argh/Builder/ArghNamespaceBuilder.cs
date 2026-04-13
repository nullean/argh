using Nullean.Argh.Middleware;
using Nullean.Argh.Runtime;

namespace Nullean.Argh.Builder;

/// <summary>
/// <see cref="IArghNamespaceBuilder"/> implementation wrapping an <see cref="ArghBuilder"/> for the same child <see cref="ArghApp"/>.
/// </summary>
public sealed class ArghNamespaceBuilder : IArghNamespaceBuilder
{
	private readonly ArghBuilder _inner;

	internal ArghNamespaceBuilder(ArghApp app, string segment)
	{
		_inner = new ArghBuilder(app);
		Segment = segment;
	}

	/// <inheritdoc />
	public string Segment { get; }

	public IArghNamespaceBuilder GlobalOptions<T>() where T : class
	{
		_ = _inner.GlobalOptions<T>();
		return this;
	}

	public IArghNamespaceBuilder Add(string name, Delegate handler)
	{
		_ = _inner.Add(name, handler);
		return this;
	}

	public IArghNamespaceBuilder Add<T>() where T : class
	{
		_ = _inner.Add<T>();
		return this;
	}

	public IArghNamespaceBuilder AddRootCommand(Delegate handler)
	{
		_ = _inner.AddRootCommand(handler);
		return this;
	}

	public IArghNamespaceBuilder AddNamespaceRootCommand(Delegate handler)
	{
		_ = _inner.AddNamespaceRootCommand(handler);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = _inner.AddNamespace(name, description, configure);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace<TNs>(string name) where TNs : class
	{
		_ = _inner.App.AddNamespace<TNs>(name);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace<TNs>(string name, Action<IArghBuilder> configure) where TNs : class
	{
		_ = _inner.AddNamespace<TNs>(name, configure);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace<TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _inner.App.AddNamespace<TNs>(name, configure);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace<TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _inner.App.AddNamespace<TNs>(configure);
		return this;
	}

	public IArghNamespaceBuilder CommandNamespaceOptions<T>() where T : class
	{
		_ = _inner.CommandNamespaceOptions<T>();
		return this;
	}

	public IArghNamespaceBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		_ = _inner.UseMiddleware(middleware);
		return this;
	}

	public IArghNamespaceBuilder UseMiddleware<TMiddleware>() where TMiddleware : ICommandMiddleware
	{
		_ = _inner.UseMiddleware<TMiddleware>();
		return this;
	}

	public Task<int> RunAsync(string[] args) => _inner.RunAsync(args);

	IArghBuilder IArghBuilder.GlobalOptions<T>() where T : class => GlobalOptions<T>();

	IArghBuilder IArghBuilder.Add(string name, Delegate handler) => Add(name, handler);

	IArghBuilder IArghBuilder.Add<T>() where T : class => Add<T>();

	IArghBuilder IArghBuilder.AddRootCommand(Delegate handler) => AddRootCommand(handler);

	IArghBuilder IArghBuilder.AddNamespaceRootCommand(Delegate handler) => AddNamespaceRootCommand(handler);

	IArghBuilder IArghBuilder.AddNamespace(string name, string description, Action<IArghBuilder> configure) =>
		AddNamespace(name, description, configure);

	IArghBuilder IArghBuilder.AddNamespace<TNs>(string name) where TNs : class => AddNamespace<TNs>(name);

	IArghBuilder IArghBuilder.AddNamespace<TNs>(string name, Action<IArghBuilder> configure) where TNs : class =>
		AddNamespace<TNs>(name, configure);

	IArghBuilder IArghBuilder.AddNamespace<TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class =>
		AddNamespace<TNs>(name, configure);

	IArghBuilder IArghBuilder.AddNamespace<TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class =>
		AddNamespace<TNs>(configure);

	IArghBuilder IArghBuilder.CommandNamespaceOptions<T>() where T : class => CommandNamespaceOptions<T>();

	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware) =>
		UseMiddleware(middleware);

	IArghBuilder IArghBuilder.UseMiddleware<TMiddleware>() => UseMiddleware<TMiddleware>();

	Task<int> IArghBuilder.RunAsync(string[] args) => RunAsync(args);
}
