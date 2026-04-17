using Nullean.Argh.Middleware;

namespace Nullean.Argh.Builder;

/// <summary>
/// Fluent registration surface for CLI commands and namespaces. Call sites are analyzed by the source generator;
/// concrete implementations forward to the app model (no-op at runtime for the analyzed surface).
/// </summary>
public interface IArghBuilder
{
	/// <summary>Registers typed global options for the current scope.</summary>
	IArghBuilder UseGlobalOptions<T>() where T : class;

	/// <summary>Registers a named command backed by a method group or lambda.</summary>
	IArghBuilder Map(string name, Delegate handler);

	/// <summary>Registers every public method on <typeparamref name="T"/> as a command.</summary>
	IArghBuilder Map<T>() where T : class;

	/// <summary>Registers a default handler when no subcommand is given at the current scope (app root or inside a namespace).</summary>
	IArghBuilder MapRoot(Delegate handler);

	/// <summary>Registers a nested command namespace with description and nested configuration.</summary>
	IArghBuilder MapNamespace(string name, string description, Action<IArghBuilder> configure);

	/// <summary>Registers a nested namespace for handler type <typeparamref name="T"/> with an explicit segment name.</summary>
	IArghBuilder MapNamespace<T>(string name) where T : class;

	/// <summary>Registers a nested namespace with nested configuration.</summary>
	IArghBuilder MapNamespace<T>(string name, Action<IArghBuilder> configure) where T : class;

	/// <summary>Registers a nested namespace with namespace-builder configuration.</summary>
	IArghBuilder MapNamespace<T>(string name, Action<IArghNamespaceBuilder> configure) where T : class;

	/// <summary>Registers a nested namespace using a segment from handler metadata.</summary>
	IArghBuilder MapNamespace<T>(Action<IArghNamespaceBuilder> configure) where T : class;

	/// <summary>Registers typed options for the current command namespace.</summary>
	IArghBuilder UseNamespaceOptions<T>() where T : class;

	/// <summary>Registers inline middleware for the current scope.</summary>
	IArghBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware);

	/// <summary>Registers middleware type <typeparamref name="TMiddleware"/> for the current scope.</summary>
	IArghBuilder UseMiddleware<TMiddleware>() where TMiddleware : ICommandMiddleware;

	/// <summary>Runs the CLI with the given arguments.</summary>
	Task<int> RunAsync(string[] args);
}
