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

	/// <summary>Initializes a new <see cref="ArghHostingBuilder"/> backed by <paramref name="services"/>.</summary>
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

	/// <inheritdoc />
	public IArghHostingBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>(ServiceLifetime.Transient);

	/// <inheritdoc />
	public IArghHostingBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(ServiceLifetime lifetime) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), lifetime));
		_ = _inner.Map<T>();
		return this;
	}

	/// <inheritdoc />
	public IArghHostingBuilder MapTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>(ServiceLifetime.Transient);

	/// <inheritdoc />
	public IArghHostingBuilder MapScoped<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>(ServiceLifetime.Scoped);

	/// <inheritdoc />
	public IArghHostingBuilder MapSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>(ServiceLifetime.Singleton);

	/// <inheritdoc />
	public IArghHostingBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		UseGlobalOptions<T>(ServiceLifetime.Transient);

	/// <inheritdoc />
	public IArghHostingBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(ServiceLifetime lifetime) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), lifetime));
		_ = _inner.UseGlobalOptions<T>();
		return this;
	}

	/// <inheritdoc />
	public IArghHostingBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() where TMiddleware : ICommandMiddleware =>
		UseMiddleware<TMiddleware>(ServiceLifetime.Transient);

	/// <inheritdoc />
	public IArghHostingBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>(ServiceLifetime lifetime) where TMiddleware : ICommandMiddleware
	{
		_services.Add(new ServiceDescriptor(typeof(TMiddleware), typeof(TMiddleware), lifetime));
		_ = _inner.UseMiddleware<TMiddleware>();
		return this;
	}

	/// <inheritdoc />
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

	/// <inheritdoc />
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

	/// <inheritdoc />
	public IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name) where T : class =>
		MapNamespace<T>(name, static _ => { });

	/// <inheritdoc />
	public IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghBuilder> configure) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Transient));
		var childApp = _inner.App.CreateChildApp(name);
		configure(new ArghHostingBuilder(_services, childApp));
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = UseGlobalOptions<T>();
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.Map(string name, Delegate handler)
	{
		_ = _inner.Map(name, handler);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		Map<T>();

	/// <inheritdoc />
	IArghRootBuilder IArghRootBuilder.UseCliDescription(string description)
	{
		_ = _inner.UseCliDescription(description);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapRoot(Delegate handler)
	{
		_ = _inner.MapRoot(handler);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapAndRootAlias<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Transient));
		_ = _inner.MapAndRootAlias<T>();
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace(string name, string description, Action<IArghBuilder> configure) =>
		MapNamespace(name, description, configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name) where T : class =>
		MapNamespace<T>(name);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghBuilder> configure) where T : class =>
		MapNamespace<T>(name, configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghNamespaceBuilder> configure) where T : class =>
		MapNamespace<T>(name, configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(Action<IArghNamespaceBuilder> configure) where T : class =>
		MapNamespace<T>(configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _inner.UseNamespaceOptions<T>();
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		_ = _inner.UseMiddleware(middleware);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
	{
		_ = UseMiddleware<TMiddleware>();
		return this;
	}

	/// <inheritdoc cref="ArghApp.RunAsync"/>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);
}
