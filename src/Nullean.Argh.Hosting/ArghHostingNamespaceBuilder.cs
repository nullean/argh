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

	public IArghNamespaceBuilder GlobalOptions<T>() where T : class
	{
		_ = _host.GlobalOptions<T>();
		return this;
	}

	public IArghNamespaceBuilder Add(string name, Delegate handler)
	{
		((IArghBuilder)_host).Add(name, handler);
		return this;
	}

	public IArghNamespaceBuilder Add<T>() where T : class
	{
		_ = _host.Add<T>();
		return this;
	}

	public IArghNamespaceBuilder AddRootCommand(Delegate handler)
	{
		((IArghBuilder)_host).AddRootCommand(handler);
		return this;
	}

	public IArghNamespaceBuilder AddNamespaceRootCommand(Delegate handler)
	{
		((IArghBuilder)_host).AddNamespaceRootCommand(handler);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = _host.AddNamespace(name, description, configure);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace<TNs>(string name) where TNs : class
	{
		_ = _host.AddNamespace<TNs>(name);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace<TNs>(string name, Action<IArghBuilder> configure) where TNs : class
	{
		_ = _host.AddNamespace<TNs>(name, configure);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace<TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _host.AddNamespace<TNs>(name, configure);
		return this;
	}

	public IArghNamespaceBuilder AddNamespace<TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _host.AddNamespace<TNs>(configure);
		return this;
	}

	public IArghNamespaceBuilder CommandNamespaceOptions<T>() where T : class
	{
		((IArghBuilder)_host).CommandNamespaceOptions<T>();
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
