using Microsoft.Extensions.DependencyInjection;
using Nullean.Argh;
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

	private ArghHostingBuilder(IServiceCollection services, ArghApp app)
	{
		_services = services ?? throw new ArgumentNullException(nameof(services));
		_inner = new ArghBuilder(app);
	}

	public IArghHostingBuilder Add<T>() where T : class =>
		Add<T>(ServiceLifetime.Transient);

	public IArghHostingBuilder Add<T>(ServiceLifetime lifetime) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), lifetime));
		_ = _inner.Add<T>();
		return this;
	}

	public IArghHostingBuilder AddTransient<T>() where T : class =>
		Add<T>(ServiceLifetime.Transient);

	public IArghHostingBuilder AddScoped<T>() where T : class =>
		Add<T>(ServiceLifetime.Scoped);

	public IArghHostingBuilder AddSingleton<T>() where T : class =>
		Add<T>(ServiceLifetime.Singleton);

	public IArghHostingBuilder AddNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = description;
		var childApp = _inner.App.CreateChildApp(name);
		configure(new ArghHostingBuilder(_services, childApp));
		return this;
	}

	/// <inheritdoc cref="IArghBuilder.AddNamespace{T}(string, Action{IArghBuilder})"/>
	/// <remarks>Registers <typeparamref name="T"/> with the same default lifetime as <see cref="Add{T}()"/> so handler instances resolve from DI without a separate <c>Add&lt;T&gt;()</c> inside the callback.</remarks>
	public IArghHostingBuilder AddNamespace<T>(string name, Action<IArghBuilder> configure) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Transient));
		var childApp = _inner.App.CreateChildApp(name);
		configure(new ArghHostingBuilder(_services, childApp));
		return this;
	}

	IArghBuilder IArghBuilder.GlobalOptions<T>() where T : class
	{
		_ = _inner.GlobalOptions<T>();
		return this;
	}

	IArghBuilder IArghBuilder.Add(string name, Delegate handler)
	{
		_ = _inner.Add(name, handler);
		return this;
	}

	IArghBuilder IArghBuilder.Add<T>() where T : class =>
		Add<T>();

	IArghBuilder IArghBuilder.AddRootCommand(Delegate handler)
	{
		_ = _inner.AddRootCommand(handler);
		return this;
	}

	IArghBuilder IArghBuilder.AddNamespaceRootCommand(Delegate handler)
	{
		_ = _inner.AddNamespaceRootCommand(handler);
		return this;
	}

	IArghBuilder IArghBuilder.AddNamespace(string name, string description, Action<IArghBuilder> configure) =>
		AddNamespace(name, description, configure);

	IArghBuilder IArghBuilder.AddNamespace<T>(string name, Action<IArghBuilder> configure) where T : class =>
		AddNamespace<T>(name, configure);

	IArghBuilder IArghBuilder.CommandNamespaceOptions<T>() where T : class
	{
		_ = _inner.CommandNamespaceOptions<T>();
		return this;
	}

	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		_ = _inner.UseMiddleware(middleware);
		return this;
	}

	IArghBuilder IArghBuilder.UseMiddleware<TMiddleware>()
	{
		_ = _inner.UseMiddleware<TMiddleware>();
		return this;
	}

	/// <inheritdoc cref="ArghApp.RunAsync"/>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);
}
