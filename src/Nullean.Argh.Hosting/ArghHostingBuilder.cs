using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Argh.Builder;
using Nullean.Argh.Middleware;
using Nullean.Argh.Runtime;

namespace Nullean.Argh.Hosting;

/// <summary>
/// Bridges fluent CLI registration (<see cref="ArghApp"/> analysis for the source generator) with <see cref="IServiceCollection"/> so command handler types are available to DI.
/// </summary>
public sealed class ArghHostingBuilder : IArghHostingBuilder
{
	private readonly IServiceCollection _services;
	private readonly ArghBuilder _inner;

	public ArghHostingBuilder(IServiceCollection services)
	{
		_services = services ?? throw new ArgumentNullException(nameof(services));
		_inner = new ArghBuilder();
	}

	internal ArghHostingBuilder(IServiceCollection services, ArghApp app)
	{
		_services = services ?? throw new ArgumentNullException(nameof(services));
		_inner = new ArghBuilder(app);
	}

	public IArghHostingBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>(ServiceLifetime.Transient);

	public IArghHostingBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(ServiceLifetime lifetime) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), lifetime));
		_ = _inner.Map<T>();
		return this;
	}

	public IArghHostingBuilder MapTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>(ServiceLifetime.Transient);

	public IArghHostingBuilder MapScoped<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>(ServiceLifetime.Scoped);

	public IArghHostingBuilder MapSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>(ServiceLifetime.Singleton);

	public IArghHostingBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		UseGlobalOptions<T>(ServiceLifetime.Transient);

	public IArghHostingBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(ServiceLifetime lifetime) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), lifetime));
		_ = _inner.UseGlobalOptions<T>();
		return this;
	}

	public IArghHostingBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() where TMiddleware : ICommandMiddleware =>
		UseMiddleware<TMiddleware>(ServiceLifetime.Transient);

	public IArghHostingBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>(ServiceLifetime lifetime) where TMiddleware : ICommandMiddleware
	{
		_services.Add(new ServiceDescriptor(typeof(TMiddleware), typeof(TMiddleware), lifetime));
		_ = _inner.UseMiddleware<TMiddleware>();
		return this;
	}

	public IArghHostingBuilder MapNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = description;
		var childApp = _inner.App.CreateChildApp(name);
		configure(new ArghHostingBuilder(_services, childApp));
		return this;
	}

	/// <inheritdoc cref="IArghBuilder.MapNamespace{T}(string, Action{IArghNamespaceBuilder})"/>
	/// <remarks>Registers <typeparamref name="T"/> with the same default lifetime as <see cref="Map{T}()"/> so handler instances resolve from DI without a separate <c>Map&lt;T&gt;()</c> inside the callback.</remarks>

	public IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghNamespaceBuilder> configure) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Transient));
		var childApp = _inner.App.CreateChildApp(name);
		configure(new ArghHostingNamespaceBuilder(_services, childApp, name));
		return this;
	}

	public IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(Action<IArghNamespaceBuilder> configure) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Transient));
		var seg = ArghNamespaceSegmentCodegen.Get<T>();
		if (seg is null)
			throw new InvalidOperationException(
				"MapNamespace<" + typeof(T).Name + ">(Action<IArghNamespaceBuilder>) requires the Argh source generator to emit a namespace segment for this type. Use MapNamespace<" + typeof(T).Name + ">(string name, ...) with an explicit segment, or ensure the project references Nullean.Argh.Core (analyzer).");

		var childApp = _inner.App.CreateChildApp(seg);
		configure(new ArghHostingNamespaceBuilder(_services, childApp, seg));
		return this;
	}

	public IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name) where T : class =>
		MapNamespace<T>(name, static _ => { });

	public IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghBuilder> configure) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Transient));
		var childApp = _inner.App.CreateChildApp(name);
		configure(new ArghHostingBuilder(_services, childApp));
		return this;
	}

	IArghBuilder IArghBuilder.UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = UseGlobalOptions<T>();
		return this;
	}

	IArghBuilder IArghBuilder.Map(string name, Delegate handler)
	{
		_ = _inner.Map(name, handler);
		return this;
	}

	IArghBuilder IArghBuilder.Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>();

	IArghBuilder IArghBuilder.MapRoot(Delegate handler)
	{
		_ = _inner.MapRoot(handler);
		return this;
	}

	IArghBuilder IArghBuilder.MapNamespace(string name, string description, Action<IArghBuilder> configure) =>
		MapNamespace(name, description, configure);

	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name) where T : class =>
		MapNamespace<T>(name);

	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghBuilder> configure) where T : class =>
		MapNamespace<T>(name, configure);

	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghNamespaceBuilder> configure) where T : class =>
		MapNamespace<T>(name, configure);

	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(Action<IArghNamespaceBuilder> configure) where T : class =>
		MapNamespace<T>(configure);

	IArghBuilder IArghBuilder.UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _inner.UseNamespaceOptions<T>();
		return this;
	}

	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		_ = _inner.UseMiddleware(middleware);
		return this;
	}

	IArghBuilder IArghBuilder.UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
	{
		_ = UseMiddleware<TMiddleware>();
		return this;
	}

	/// <inheritdoc cref="ArghApp.RunAsync"/>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);
}
