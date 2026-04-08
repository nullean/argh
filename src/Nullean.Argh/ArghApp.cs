namespace Nullean.Argh;

/// <summary>
/// Fluent registration surface. Call sites are analyzed by the source generator; these methods are no-ops at runtime.
/// </summary>
public sealed partial class ArghApp : IArghBuilder
{
	private readonly string _commandNamespacePath;
	private static readonly Dictionary<string, Delegate> Lambdas = new(StringComparer.OrdinalIgnoreCase);

	public ArghApp() => _commandNamespacePath = "";
	private ArghApp(string commandNamespacePath) => _commandNamespacePath = commandNamespacePath;

	/// <summary>Parses typed options before routing; available to all commands (see command namespace options).</summary>
	public ArghApp GlobalOptions<T>() where T : class => this;

	/// <summary>Registers a named command backed by a method group or lambda.</summary>
	public ArghApp Add(string name, Delegate handler)
	{
		var key = _commandNamespacePath.Length == 0 ? name : _commandNamespacePath + "/" + name;
		Lambdas[key] = handler;
		return this;
	}

	/// <summary>Registers every public method on <typeparamref name="T"/> as a command.</summary>
	public ArghApp Add<T>() where T : class => this;

	/// <summary>Creates a nested command namespace (ASP.NET <c>MapGroup</c> style).</summary>
	public ArghApp AddNamespace(string name, Action<IArghBuilder> configure)
	{
		configure(new ArghBuilder(CreateChildApp(name)));
		return this;
	}

	/// <summary>Creates an <see cref="ArghApp"/> for a child namespace segment (same path rules as <see cref="AddNamespace"/>).</summary>
	public ArghApp CreateChildApp(string name)
	{
		var childPath = _commandNamespacePath.Length == 0 ? name : _commandNamespacePath + "/" + name;
		return new ArghApp(childPath);
	}

	/// <summary>Typed options for the current namespace; <typeparamref name="T"/> must inherit the parent options type (enforced at compile time).</summary>
	public ArghApp CommandNamespaceOptions<T>() where T : class => this;

	/// <summary>Runs the source-generated CLI for this application (see <see cref="ArghRuntime.RunAsync"/>).</summary>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);

	/// <summary>Gets the delegate stored for a lambda-registered command by its storage key.</summary>
	public static Delegate? GetRegisteredLambda(string key) =>
		Lambdas.TryGetValue(key, out var h) ? h : null;
}
