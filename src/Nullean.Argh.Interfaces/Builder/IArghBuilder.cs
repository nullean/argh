using System.Diagnostics.CodeAnalysis;
using Nullean.Argh.Middleware;

namespace Nullean.Argh.Builder;

/// <summary>
/// Fluent registration surface for CLI commands and namespaces. Call sites are analyzed by the source generator;
/// concrete implementations forward to the app model (no-op at runtime for the analyzed surface).
/// </summary>
public interface IArghBuilder
{
	/// <summary>Registers typed global options for the current scope.</summary>
	IArghBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class;

	/// <summary>Registers a named command backed by a method group or lambda.</summary>
	IArghBuilder Map(string name, Delegate handler);

	/// <summary>
	/// Hoists every public method on <typeparamref name="T"/> as a command into the current scope (root or namespace).
	/// Methods are named by converting PascalCase to kebab-case; use <see cref="CommandNameAttribute"/> to override a name.
	/// Annotate one method with <see cref="DefaultCommandAttribute"/> to make it the scope's default handler.
	/// </summary>
	IArghBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class;

	/// <summary>Registers a default handler when no subcommand is given at the current scope (app root or inside a namespace).</summary>
	IArghBuilder MapRoot(Delegate handler);

	/// <summary>Registers a nested command namespace with an explicit description and nested configuration.</summary>
	IArghBuilder MapNamespace(string name, string description, Action<IArghBuilder> configure);

	/// <summary>
	/// Registers a nested namespace named <paramref name="name"/> whose description and commands come from <typeparamref name="T"/>.
	/// Every public method on <typeparamref name="T"/> becomes a command inside the namespace.
	/// </summary>
	IArghBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name) where T : class;

	/// <summary>
	/// Registers a nested namespace named <paramref name="name"/> sourced from <typeparamref name="T"/>, with additional configuration.
	/// Every public method on <typeparamref name="T"/> becomes a command inside the namespace.
	/// </summary>
	IArghBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghBuilder> configure) where T : class;

	/// <summary>
	/// Registers a nested namespace named <paramref name="name"/> sourced from <typeparamref name="T"/>, with namespace-builder configuration.
	/// Every public method on <typeparamref name="T"/> becomes a command inside the namespace.
	/// </summary>
	IArghBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghNamespaceBuilder> configure) where T : class;

	/// <summary>
	/// Registers a nested namespace sourced from <typeparamref name="T"/>; the segment name is read from
	/// <see cref="NamespaceSegmentAttribute"/> on <typeparamref name="T"/>.
	/// Every public method on <typeparamref name="T"/> becomes a command inside the namespace.
	/// </summary>
	IArghBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(Action<IArghNamespaceBuilder> configure) where T : class;

	/// <summary>
	/// Hoists every public method on <typeparamref name="T"/> as a named command into the current scope, and designates
	/// one of them as the scope's root alias — the command that runs when no subcommand is given.
	/// If <typeparamref name="T"/> has a single public method it becomes the alias target automatically.
	/// If <typeparamref name="T"/> has multiple public methods, annotate exactly one with <see cref="DefaultCommandAttribute"/>.
	/// Unlike <see cref="MapRoot"/>, the alias target remains accessible as a normal named command.
	/// </summary>
	IArghBuilder MapAndRootAlias<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class;

	/// <summary>Registers typed options for the current command namespace.</summary>
	IArghBuilder UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class;

	/// <summary>Registers inline middleware for the current scope.</summary>
	IArghBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware);

	/// <summary>Registers middleware type <typeparamref name="TMiddleware"/> for the current scope.</summary>
	IArghBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() where TMiddleware : ICommandMiddleware;

	/// <summary>Runs the CLI with the given arguments.</summary>
	Task<int> RunAsync(string[] args);
}
