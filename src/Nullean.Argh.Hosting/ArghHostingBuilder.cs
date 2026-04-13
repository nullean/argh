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

	public IArghHostingBuilder GlobalOptions<T>() where T : class =>
		GlobalOptions<T>(ServiceLifetime.Transient);

	public IArghHostingBuilder GlobalOptions<T>(ServiceLifetime lifetime) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), lifetime));
		_ = _inner.GlobalOptions<T>();
		return this;
	}

	public IArghHostingBuilder UseMiddleware<TMiddleware>() where TMiddleware : ICommandMiddleware =>
		UseMiddleware<TMiddleware>(ServiceLifetime.Transient);

	public IArghHostingBuilder UseMiddleware<TMiddleware>(ServiceLifetime lifetime) where TMiddleware : ICommandMiddleware
	{
		_services.Add(new ServiceDescriptor(typeof(TMiddleware), typeof(TMiddleware), lifetime));
		_ = _inner.UseMiddleware<TMiddleware>();
		return this;
	}

	public IArghHostingBuilder AddNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = description;
		var childApp = _inner.App.CreateChildApp(name);
		configure(new ArghHostingBuilder(_services, childApp));
		return this;
	}

	/// <inheritdoc cref="IArghBuilder.AddNamespace{T}(string, Action{IArghBuilder})"/>
	/// <remarks>Registers <typeparamref name="T"/> with the same default lifetime as <see cref="Add{T}()"/> so handler instances resolve from DI without a separate <c>Add&lt;T&gt;()</c> inside the callback.</remarks>

	public IArghHostingBuilder AddNamespace<T>(string name, Action<IArghNamespaceBuilder> configure) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Transient));
		var childApp = _inner.App.CreateChildApp(name);
		configure(new ArghHostingNamespaceBuilder(_services, childApp, name));
		return this;
	}

	public IArghHostingBuilder AddNamespace<T>(Action<IArghNamespaceBuilder> configure) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Transient));
		var seg = ArghNamespaceSegmentCodegen.Get<T>();
		if (seg is null)
			throw new InvalidOperationException(
				"AddNamespace<" + typeof(T).Name + ">(Action<IArghNamespaceBuilder>) requires the Argh source generator to emit a namespace segment for this type. Use AddNamespace<" + typeof(T).Name + ">(string name, ...) with an explicit segment, or ensure the project references Nullean.Argh.Generator.");

		var childApp = _inner.App.CreateChildApp(seg);
		configure(new ArghHostingNamespaceBuilder(_services, childApp, seg));
		return this;
	}

	public IArghHostingBuilder AddNamespace<T>(string name) where T : class =>
		AddNamespace<T>(name, static _ => { });

	public IArghHostingBuilder AddNamespace<T>(string name, Action<IArghBuilder> configure) where T : class
	{
		_services.Add(new ServiceDescriptor(typeof(T), typeof(T), ServiceLifetime.Transient));
		var childApp = _inner.App.CreateChildApp(name);
		configure(new ArghHostingBuilder(_services, childApp));
		return this;
	}

	IArghBuilder IArghBuilder.GlobalOptions<T>() where T : class
	{
		_ = GlobalOptions<T>();
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

	IArghBuilder IArghBuilder.AddNamespace<T>(string name) where T : class =>
		AddNamespace<T>(name);

	IArghBuilder IArghBuilder.AddNamespace<T>(string name, Action<IArghBuilder> configure) where T : class =>
		AddNamespace<T>(name, configure);

	IArghBuilder IArghBuilder.AddNamespace<T>(string name, Action<IArghNamespaceBuilder> configure) where T : class =>
		AddNamespace<T>(name, configure);

	IArghBuilder IArghBuilder.AddNamespace<T>(Action<IArghNamespaceBuilder> configure) where T : class =>
		AddNamespace<T>(configure);

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
		_ = UseMiddleware<TMiddleware>();
		return this;
	}

	/// <inheritdoc cref="ArghApp.RunAsync"/>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);
}
