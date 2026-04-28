using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nullean.Argh.Builder;
using Nullean.Argh.Middleware;

namespace Nullean.Argh.Hosting;

/// <summary>
/// <see cref="IArghRootBuilder"/> plus DI registration for command handler types when using the generic host.
/// </summary>
public interface IArghHostingBuilder : IArghRootBuilder
{
	/// <inheritdoc cref="IArghBuilder.UseGlobalOptions{T}"/>
	/// <remarks>Also registers <typeparamref name="T"/> in DI with <see cref="ServiceLifetime.Transient"/>.</remarks>
	new IArghHostingBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class;

	/// <summary>Registers global options in DI with an explicit lifetime (default registration uses <see cref="ServiceLifetime.Transient"/>).</summary>
	IArghHostingBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(ServiceLifetime lifetime) where T : class;

	/// <inheritdoc cref="IArghBuilder.UseMiddleware{TMiddleware}"/>
	/// <remarks>Also registers <typeparamref name="TMiddleware"/> in DI with <see cref="ServiceLifetime.Transient"/>.</remarks>
	new IArghHostingBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() where TMiddleware : ICommandMiddleware;

	/// <summary>Registers middleware in DI with an explicit lifetime (default registration uses <see cref="ServiceLifetime.Transient"/>).</summary>
	IArghHostingBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>(ServiceLifetime lifetime) where TMiddleware : ICommandMiddleware;

	/// <summary>Registers command handlers from <typeparamref name="T"/> as transient services (default lifetime).</summary>
	new IArghHostingBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class;

	/// <summary>Registers command handlers from <typeparamref name="T"/> with an explicit lifetime.</summary>
	IArghHostingBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(ServiceLifetime lifetime) where T : class;

	/// <summary>Registers command handlers from <typeparamref name="T"/> as transient services (delegates to <see cref="Map{T}(ServiceLifetime)"/>).</summary>
	IArghHostingBuilder MapTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class;

	/// <summary>Registers command handlers from <typeparamref name="T"/> as scoped services (delegates to <see cref="Map{T}(ServiceLifetime)"/>).</summary>
	IArghHostingBuilder MapScoped<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class;

	/// <summary>Registers command handlers from <typeparamref name="T"/> as singleton services (delegates to <see cref="Map{T}(ServiceLifetime)"/>).</summary>
	IArghHostingBuilder MapSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class;

	/// <inheritdoc cref="IArghBuilder.MapNamespace(string, string, Action{IArghBuilder})"/>
	new IArghHostingBuilder MapNamespace(string name, string description, Action<IArghBuilder> configure);

	/// <inheritdoc cref="IArghBuilder.MapNamespace{T}(string)"/>
	new IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name) where T : class;

	/// <inheritdoc cref="IArghBuilder.MapNamespace{T}(string, Action{IArghBuilder})"/>
	new IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghBuilder> configure) where T : class;

	/// <inheritdoc cref="IArghBuilder.MapNamespace{T}(string, Action{IArghNamespaceBuilder})"/>
	new IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghNamespaceBuilder> configure) where T : class;

	/// <inheritdoc cref="IArghBuilder.MapNamespace{T}(Action{IArghNamespaceBuilder})"/>
	new IArghHostingBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(Action<IArghNamespaceBuilder> configure) where T : class;

	/// <summary>
	/// Sets the minimum log level applied to all logging providers when <c>AddArgh</c> detects an intrinsic
	/// command invocation (built-in or user-defined <c>[CommandIntrinsic]</c>). Log entries below this level
	/// are suppressed, keeping intrinsic output clean.
	/// <para>
	/// Default: <see cref="LogLevel.Warning"/> (suppresses <c>Information</c> and below).
	/// </para>
	/// <para>
	/// Set to <see cref="LogLevel.Trace"/> to re-enable all logs, or <see cref="LogLevel.None"/> to
	/// suppress all logs including errors.
	/// </para>
	/// </summary>
	IArghHostingBuilder IntrinsicLogLevelMinimum(LogLevel level);
}
