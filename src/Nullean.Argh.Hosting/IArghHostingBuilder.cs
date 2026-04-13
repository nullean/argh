using Microsoft.Extensions.DependencyInjection;
using Nullean.Argh.Builder;
using Nullean.Argh.Middleware;

namespace Nullean.Argh.Hosting;

/// <summary>
/// <see cref="IArghBuilder"/> plus DI registration for command handler types when using the generic host.
/// </summary>
public interface IArghHostingBuilder : IArghBuilder
{
	/// <inheritdoc cref="IArghBuilder.GlobalOptions{T}"/>
	/// <remarks>Also registers <typeparamref name="T"/> in DI with <see cref="ServiceLifetime.Transient"/>.</remarks>
	new IArghHostingBuilder GlobalOptions<T>() where T : class;

	/// <summary>Registers global options in DI with an explicit lifetime (default registration uses <see cref="ServiceLifetime.Transient"/>).</summary>
	IArghHostingBuilder GlobalOptions<T>(ServiceLifetime lifetime) where T : class;

	/// <inheritdoc cref="IArghBuilder.UseMiddleware{TMiddleware}"/>
	/// <remarks>Also registers <typeparamref name="TMiddleware"/> in DI with <see cref="ServiceLifetime.Transient"/>.</remarks>
	new IArghHostingBuilder UseMiddleware<TMiddleware>() where TMiddleware : ICommandMiddleware;

	/// <summary>Registers middleware in DI with an explicit lifetime (default registration uses <see cref="ServiceLifetime.Transient"/>).</summary>
	IArghHostingBuilder UseMiddleware<TMiddleware>(ServiceLifetime lifetime) where TMiddleware : ICommandMiddleware;

	/// <summary>Registers command handlers from <typeparamref name="T"/> as transient services (default lifetime).</summary>
	new IArghHostingBuilder Add<T>() where T : class;

	/// <summary>Registers command handlers from <typeparamref name="T"/> with an explicit lifetime.</summary>
	IArghHostingBuilder Add<T>(ServiceLifetime lifetime) where T : class;

	/// <summary>Registers command handlers from <typeparamref name="T"/> as transient services (delegates to <see cref="Add{T}(ServiceLifetime)"/>).</summary>
	IArghHostingBuilder AddTransient<T>() where T : class;

	/// <summary>Registers command handlers from <typeparamref name="T"/> as scoped services (delegates to <see cref="Add{T}(ServiceLifetime)"/>).</summary>
	IArghHostingBuilder AddScoped<T>() where T : class;

	/// <summary>Registers command handlers from <typeparamref name="T"/> as singleton services (delegates to <see cref="Add{T}(ServiceLifetime)"/>).</summary>
	IArghHostingBuilder AddSingleton<T>() where T : class;

	/// <inheritdoc cref="IArghBuilder.AddNamespace(string, string, Action{IArghBuilder})"/>
	new IArghHostingBuilder AddNamespace(string name, string description, Action<IArghBuilder> configure);

	/// <inheritdoc cref="IArghBuilder.AddNamespace{T}(string)"/>
	new IArghHostingBuilder AddNamespace<T>(string name) where T : class;

	/// <inheritdoc cref="IArghBuilder.AddNamespace{T}(string, Action{IArghBuilder})"/>
	new IArghHostingBuilder AddNamespace<T>(string name, Action<IArghBuilder> configure) where T : class;

	/// <inheritdoc cref="IArghBuilder.AddNamespace{T}(string, Action{IArghNamespaceBuilder})"/>
	new IArghHostingBuilder AddNamespace<T>(string name, Action<IArghNamespaceBuilder> configure) where T : class;

	/// <inheritdoc cref="IArghBuilder.AddNamespace{T}(Action{IArghNamespaceBuilder})"/>
	new IArghHostingBuilder AddNamespace<T>(Action<IArghNamespaceBuilder> configure) where T : class;

}
