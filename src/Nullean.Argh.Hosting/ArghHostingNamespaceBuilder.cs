using System.Diagnostics.CodeAnalysis;
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

	/// <inheritdoc />
	public IArghNamespaceBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _host.UseGlobalOptions<T>();
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder Map(string name, Delegate handler)
	{
		((IArghBuilder)_host).Map(name, handler);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _host.Map<T>();
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapRoot(Delegate handler)
	{
		((IArghBuilder)_host).MapRoot(handler);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = _host.MapNamespace(name, description, configure);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name) where TNs : class
	{
		_ = _host.MapNamespace<TNs>(name);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghBuilder> configure) where TNs : class
	{
		_ = _host.MapNamespace<TNs>(name, configure);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _host.MapNamespace<TNs>(name, configure);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _host.MapNamespace<TNs>(configure);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		((IArghBuilder)_host).UseNamespaceOptions<T>();
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		((IArghBuilder)_host).UseMiddleware(middleware);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() where TMiddleware : ICommandMiddleware
	{
		_ = _host.UseMiddleware<TMiddleware>();
		return this;
	}

	/// <inheritdoc />
	public Task<int> RunAsync(string[] args) => _host.RunAsync(args);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		UseGlobalOptions<T>();

	/// <inheritdoc />
	IArghBuilder IArghBuilder.Map(string name, Delegate handler) => Map(name, handler);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class => Map<T>();

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapRoot(Delegate handler) => MapRoot(handler);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapAndRootAlias<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		((IArghBuilder)_host).MapAndRootAlias<T>();
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace(string name, string description, Action<IArghBuilder> configure) =>
		MapNamespace(name, description, configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name) where TNs : class =>
		MapNamespace<TNs>(name);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(name, configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(name, configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		UseNamespaceOptions<T>();

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware) =>
		UseMiddleware(middleware);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() =>
		UseMiddleware<TMiddleware>();

	/// <inheritdoc />
	Task<int> IArghBuilder.RunAsync(string[] args) => RunAsync(args);
}
