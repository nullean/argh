namespace Nullean.Argh;

/// <summary>
/// Fluent registration surface. Call sites are analyzed by the source generator; these methods are no-ops at runtime.
/// </summary>
public sealed partial class ArghApp : IArghBuilder
{
	private readonly string _groupPath;
	private static readonly Dictionary<string, Delegate> s_lambdas =
		new(StringComparer.OrdinalIgnoreCase);

	public ArghApp() => _groupPath = "";
	private ArghApp(string groupPath) => _groupPath = groupPath;

	/// <summary>Parses typed options before routing; available to all commands (see group options).</summary>
	public ArghApp GlobalOptions<T>() where T : class => this;

	/// <summary>Registers a named command backed by a method group or lambda.</summary>
	public ArghApp Add(string name, Delegate handler)
	{
		var key = _groupPath.Length == 0 ? name : _groupPath + "/" + name;
		s_lambdas[key] = handler;
		return this;
	}

	/// <summary>Registers every public method on <typeparamref name="T"/> as a command.</summary>
	public ArghApp Add<T>() where T : class => this;

	/// <summary>Creates a nested command group (ASP.NET <c>MapGroup</c> style).</summary>
	public ArghApp Group(string name, Action<ArghApp> configure)
	{
		var childPath = _groupPath.Length == 0 ? name : _groupPath + "/" + name;
		if (childPath == null) throw new ArgumentNullException(nameof(childPath));
		configure(new ArghApp(childPath));
		return this;
	}

	/// <summary>Typed options for the current group; <typeparamref name="T"/> must inherit the parent options type (enforced at compile time).</summary>
	public ArghApp GroupOptions<T>() where T : class => this;

	/// <summary>Runs the source-generated CLI for this application (see <see cref="ArghRuntime.RunAsync"/>).</summary>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);

	/// <summary>Gets the delegate stored for a lambda-registered command by its storage key.</summary>
	public static Delegate? GetRegisteredLambda(string key) =>
		s_lambdas.TryGetValue(key, out var h) ? h : null;
}
