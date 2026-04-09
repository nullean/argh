using Nullean.Argh;
using Nullean.Argh.Filters;

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

	/// <inheritdoc cref="ArghApp.UseFilter(System.Func{CommandContext, CommandFilterDelegate, System.Threading.Tasks.ValueTask})"/>
	IArghBuilder UseFilter(Func<CommandContext, CommandFilterDelegate, ValueTask> filter);

	/// <inheritdoc cref="ArghApp.UseFilter{TFilter}"/>
	IArghBuilder UseFilter<TFilter>() where TFilter : ICommandFilter;

	/// <inheritdoc cref="ArghApp.RunAsync"/>
	Task<int> RunAsync(string[] args);
}
