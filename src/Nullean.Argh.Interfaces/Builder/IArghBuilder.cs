using Nullean.Argh.Middleware;

namespace Nullean.Argh.Builder;

/// <summary>
/// Fluent registration surface for CLI commands and namespaces. Call sites are analyzed by the source generator;
/// concrete implementations forward to the app model (no-op at runtime for the analyzed surface).
/// </summary>
public interface IArghBuilder
{
	/// <summary>Registers typed global options for the current scope.</summary>
	IArghBuilder GlobalOptions<T>() where T : class;

	/// <summary>Registers a named command backed by a method group or lambda.</summary>
	IArghBuilder Add(string name, Delegate handler);

	/// <summary>Registers every public method on <typeparamref name="T"/> as a command.</summary>
	IArghBuilder Add<T>() where T : class;

	/// <summary>Registers a default handler when no subcommand is given at the root.</summary>
	IArghBuilder AddRootCommand(Delegate handler);

	/// <summary>Registers a default handler for the current namespace when no deeper subcommand is given.</summary>
	IArghBuilder AddNamespaceRootCommand(Delegate handler);

	/// <summary>Registers a nested command namespace with description and nested configuration.</summary>
	IArghBuilder AddNamespace(string name, string description, Action<IArghBuilder> configure);

	/// <summary>Registers a nested namespace for handler type <typeparamref name="T"/> with an explicit segment name.</summary>
	IArghBuilder AddNamespace<T>(string name) where T : class;

	/// <summary>Registers a nested namespace with nested configuration.</summary>
	IArghBuilder AddNamespace<T>(string name, Action<IArghBuilder> configure) where T : class;

	/// <summary>Registers a nested namespace with namespace-builder configuration.</summary>
	IArghBuilder AddNamespace<T>(string name, Action<IArghNamespaceBuilder> configure) where T : class;

	/// <summary>Registers a nested namespace using a segment from handler metadata.</summary>
	IArghBuilder AddNamespace<T>(Action<IArghNamespaceBuilder> configure) where T : class;

	/// <summary>Registers typed options for the current command namespace.</summary>
	IArghBuilder CommandNamespaceOptions<T>() where T : class;

	/// <summary>Registers inline middleware for the current scope.</summary>
	IArghBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware);

	/// <summary>Registers middleware type <typeparamref name="TMiddleware"/> for the current scope.</summary>
	IArghBuilder UseMiddleware<TMiddleware>() where TMiddleware : ICommandMiddleware;

	/// <summary>Runs the CLI with the given arguments.</summary>
	Task<int> RunAsync(string[] args);
}
