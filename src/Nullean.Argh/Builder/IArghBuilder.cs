using Nullean.Argh;
using Nullean.Argh.Middleware;

namespace Nullean.Argh.Builder;

/// <summary>
/// Fluent registration surface for CLI commands and namespaces. Call sites are analyzed by the source generator;
/// implementations should forward to <see cref="ArghApp"/> (no-op at runtime for the analyzed model).
/// </summary>
public interface IArghBuilder
{
	/// <inheritdoc cref="ArghApp.GlobalOptions{T}"/>
	IArghBuilder GlobalOptions<T>() where T : class;

	/// <inheritdoc cref="ArghApp.Add(string, Delegate)"/>
	IArghBuilder Add(string name, Delegate handler);

	/// <inheritdoc cref="ArghApp.Add{T}"/>
	IArghBuilder Add<T>() where T : class;

	/// <inheritdoc cref="ArghApp.AddNamespace"/>
	IArghBuilder AddNamespace(string name, Action<IArghBuilder> configure);

	/// <inheritdoc cref="ArghApp.CommandNamespaceOptions{T}"/>
	IArghBuilder CommandNamespaceOptions<T>() where T : class;

	/// <inheritdoc cref="ArghApp.UseMiddleware(System.Func{CommandContext, CommandMiddlewareDelegate, System.Threading.Tasks.ValueTask})"/>
	IArghBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware);

	/// <inheritdoc cref="ArghApp.UseMiddleware{TMiddleware}"/>
	IArghBuilder UseMiddleware<TMiddleware>() where TMiddleware : ICommandMiddleware;

	/// <inheritdoc cref="ArghApp.RunAsync"/>
	Task<int> RunAsync(string[] args);
}
