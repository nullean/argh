using Microsoft.Extensions.DependencyInjection;
using Nullean.Argh;
using Nullean.Argh.Builder;
using Nullean.Argh.Middleware;
using Nullean.Argh.Runtime;

namespace Nullean.Argh.Hosting;

/// <summary>
/// <see cref="IArghNamespaceBuilder"/> for hosted registration: forwards to <see cref="ArghHostingBuilder"/> and exposes <see cref="Segment"/>.
/// </summary>
public sealed class ArghHostingNamespaceBuilder : IArghNamespaceBuilder
{
	private readonly ArghHostingBuilder _host;

	internal ArghHostingNamespaceBuilder(IServiceCollection services, ArghApp childApp, string segment)
	{
		_host = new ArghHostingBuilder(services, childApp);
		Segment = segment;
	}

	/// <inheritdoc />
	public string Segment { get; }

	public IArghNamespaceBuilder UseGlobalOptions<T>() where T : class
	{
		_ = _host.UseGlobalOptions<T>();
		return this;
	}

	public IArghNamespaceBuilder Map(string name, Delegate handler)
	{
		((IArghBuilder)_host).Map(name, handler);
		return this;
	}

	public IArghNamespaceBuilder Map<T>() where T : class
	{
		_ = _host.Map<T>();
		return this;
	}

	public IArghNamespaceBuilder MapRoot(Delegate handler)
	{
		((IArghBuilder)_host).MapRoot(handler);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = _host.MapNamespace(name, description, configure);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace<TNs>(string name) where TNs : class
	{
		_ = _host.MapNamespace<TNs>(name);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace<TNs>(string name, Action<IArghBuilder> configure) where TNs : class
	{
		_ = _host.MapNamespace<TNs>(name, configure);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace<TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _host.MapNamespace<TNs>(name, configure);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace<TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _host.MapNamespace<TNs>(configure);
		return this;
	}

	public IArghNamespaceBuilder UseNamespaceOptions<T>() where T : class
	{
		((IArghBuilder)_host).UseNamespaceOptions<T>();
		return this;
	}

	public IArghNamespaceBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		((IArghBuilder)_host).UseMiddleware(middleware);
		return this;
	}

	public IArghNamespaceBuilder UseMiddleware<TMiddleware>() where TMiddleware : ICommandMiddleware
	{
		_ = _host.UseMiddleware<TMiddleware>();
		return this;
	}

	public Task<int> RunAsync(string[] args) => _host.RunAsync(args);

	IArghBuilder IArghBuilder.UseGlobalOptions<T>() where T : class => UseGlobalOptions<T>();

	IArghBuilder IArghBuilder.Map(string name, Delegate handler) => Map(name, handler);

	IArghBuilder IArghBuilder.Map<T>() where T : class => Map<T>();

	IArghBuilder IArghBuilder.MapRoot(Delegate handler) => MapRoot(handler);

	IArghBuilder IArghBuilder.MapNamespace(string name, string description, Action<IArghBuilder> configure) =>
		MapNamespace(name, description, configure);

	IArghBuilder IArghBuilder.MapNamespace<TNs>(string name) where TNs : class => MapNamespace<TNs>(name);

	IArghBuilder IArghBuilder.MapNamespace<TNs>(string name, Action<IArghBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(name, configure);

	IArghBuilder IArghBuilder.MapNamespace<TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(name, configure);

	IArghBuilder IArghBuilder.MapNamespace<TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(configure);

	IArghBuilder IArghBuilder.UseNamespaceOptions<T>() where T : class => UseNamespaceOptions<T>();

	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware) =>
		UseMiddleware(middleware);

	IArghBuilder IArghBuilder.UseMiddleware<TMiddleware>() => UseMiddleware<TMiddleware>();

	Task<int> IArghBuilder.RunAsync(string[] args) => RunAsync(args);
}
