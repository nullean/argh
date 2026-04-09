using Microsoft.Extensions.DependencyInjection;
using Nullean.Argh.Builder;

namespace Nullean.Argh.Hosting;

/// <summary>
/// <see cref="IArghBuilder"/> plus DI registration for command handler types when using the generic host.
/// </summary>
public interface IArghHostingBuilder : IArghBuilder
{
	/// <summary>Registers command handlers from <typeparamref name="T"/> as transient services (default lifetime).</summary>
	new IArghHostingBuilder Add<T>() where T : class;

	/// <summary>Registers command handlers from <typeparamref name="T"/> with an explicit lifetime.</summary>
	IArghHostingBuilder Add<T>(ServiceLifetime lifetime) where T : class;

	IArghHostingBuilder AddTransient<T>() where T : class;
	IArghHostingBuilder AddScoped<T>() where T : class;
	IArghHostingBuilder AddSingleton<T>() where T : class;

	/// <inheritdoc cref="IArghBuilder.AddNamespace(string, string, Action{IArghBuilder})"/>
	new IArghHostingBuilder AddNamespace(string name, string description, Action<IArghBuilder> configure);

	/// <inheritdoc cref="IArghBuilder.AddNamespace{T}(string, Action{IArghBuilder})"/>
	new IArghHostingBuilder AddNamespace<T>(string name, Action<IArghBuilder> configure) where T : class;
}
