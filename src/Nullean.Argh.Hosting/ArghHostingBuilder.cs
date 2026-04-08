using Microsoft.Extensions.DependencyInjection;
using Nullean.Argh.Builder;
using Nullean.Argh.Filters;
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

	public IArghHostingBuilder AddNamespace(string name, Action<IArghBuilder> configure)
	{
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

	IArghBuilder IArghBuilder.AddNamespace(string name, Action<IArghBuilder> configure) =>
		AddNamespace(name, configure);

	IArghBuilder IArghBuilder.CommandNamespaceOptions<T>() where T : class
	{
		_ = _inner.CommandNamespaceOptions<T>();
		return this;
	}

	IArghBuilder IArghBuilder.UseFilter(Func<CommandContext, CommandFilterDelegate, ValueTask> filter)
	{
		_ = _inner.UseFilter(filter);
		return this;
	}

	IArghBuilder IArghBuilder.UseFilter<TFilter>()
	{
		_ = _inner.UseFilter<TFilter>();
		return this;
	}

	/// <inheritdoc cref="ArghApp.RunAsync"/>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);
}
