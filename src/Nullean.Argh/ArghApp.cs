namespace Nullean.Argh;

/// <summary>
/// Fluent registration surface. Call sites are analyzed by the source generator; these methods are no-ops at runtime.
/// </summary>
public sealed partial class ArghApp : IArghBuilder
{
	/// <summary>Parses typed options before routing; available to all commands (see group options).</summary>
	public ArghApp GlobalOptions<T>() where T : class => this;

	/// <summary>Registers a named command backed by a method group (not a capturing lambda).</summary>
	public ArghApp Add(string name, Delegate handler) => this;

	/// <summary>Registers every public method on <typeparamref name="T"/> as a command.</summary>
	public ArghApp Add<T>() where T : class => this;

	/// <summary>Creates a nested command group (ASP.NET <c>MapGroup</c> style).</summary>
	public ArghApp Group(string name, Action<ArghApp> configure) => this;

	/// <summary>Typed options for the current group; <typeparamref name="T"/> must inherit the parent options type (enforced at compile time).</summary>
	public ArghApp GroupOptions<T>() where T : class => this;
}
