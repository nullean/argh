using System.Diagnostics.CodeAnalysis;
using Nullean.Argh.Builder;
using Nullean.Argh.Help;
using Nullean.Argh.Runtime;

namespace Nullean.Argh;

/// <summary>
/// Fluent registration surface. Call sites are analyzed by the source generator; these methods are no-ops at runtime.
/// </summary>
public sealed partial class ArghApp : IArghRootBuilder
{
	private readonly string _commandNamespacePath;
	private static readonly Dictionary<string, Delegate> Lambdas = new(StringComparer.OrdinalIgnoreCase);

	public ArghApp() => _commandNamespacePath = "";
	private ArghApp(string commandNamespacePath) => _commandNamespacePath = commandNamespacePath;

	/// <summary>Parses typed options before routing; available to all commands (see namespace options).</summary>
	public ArghApp UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class => this;

	/// <summary>Registers a named command backed by a method group or lambda.</summary>
	public ArghApp Map(string name, Delegate handler)
	{
		var key = _commandNamespacePath.Length == 0 ? name : _commandNamespacePath + "/" + name;
		Lambdas[key] = handler;
		return this;
	}

	/// <summary>Registers every public method on <typeparamref name="T"/> as a command.</summary>
	public ArghApp Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class => this;

	/// <summary>Sets a one-line description shown in root <c>--help</c> output. Analyzed by the source generator; no-op at runtime.</summary>
	public ArghApp UseCliDescription(string description) => this;

	/// <summary>
	/// Registers a default handler when no subcommand or namespace segment applies at the current scope
	/// (app root or inside a <see cref="MapNamespace"/> block). Analyzed by the source generator.
	/// </summary>
	public ArghApp MapRoot(Delegate handler)
	{
		if (_commandNamespacePath.Length == 0)
			Lambdas["__argh_root"] = handler;
		else
			Lambdas[_commandNamespacePath + "/__argh_root"] = handler;
		return this;
	}

	/// <summary>
	/// Creates a nested command namespace (ASP.NET <c>MapGroup</c> style). The description is shown in root/namespace <c>--help</c> listings; use <see cref="string.Empty"/> when you want no prose.
	/// </summary>
	public ArghApp MapNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = description;
		configure(new ArghBuilder(CreateChildApp(name)));
		return this;
	}

	/// <summary>
	/// Creates a nested namespace with no configure callback; equivalent to <c>MapNamespace&lt;T&gt;(name, _ =&gt; { })</c>.
	/// </summary>
	public ArghApp MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name) where T : class =>
		MapNamespace<T>(name, static (IArghBuilder _) => { });

	/// <summary>
	/// Creates a nested namespace; the listing description is taken from the XML <c>&lt;summary&gt;</c> on <typeparamref name="T"/>.
	/// Public commands on <typeparamref name="T"/> (and nested handler classes) are registered automatically—do not call <c>Map&lt;T&gt;()</c> again inside the configure callback.
	/// </summary>
	public ArghApp MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghBuilder> configure) where T : class
	{
		configure(new ArghBuilder(CreateChildApp(name)));
		return this;
	}

	/// <summary>Like <see cref="MapNamespace{T}(string, Action{IArghBuilder})"/> but passes an <see cref="IArghNamespaceBuilder"/> that exposes <see cref="IArghNamespaceBuilder.Segment"/>.</summary>
	public ArghApp MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghNamespaceBuilder> configure) where T : class
	{
		configure(new ArghNamespaceBuilder(CreateChildApp(name), name));
		return this;
	}

	/// <summary>
	/// Nested namespace where the segment is resolved at compile time (see <see cref="ArghNamespaceSegmentCodegen"/>).
	/// Requires the source generator to emit registration for <typeparamref name="T"/>.
	/// </summary>
	public ArghApp MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(Action<IArghNamespaceBuilder> configure) where T : class
	{
		var seg = ArghNamespaceSegmentCodegen.Get<T>();
		if (seg is null)
		{
			throw new InvalidOperationException(
				"MapNamespace<" + typeof(T).Name + ">(Action<IArghNamespaceBuilder>) requires the Argh source generator to emit a namespace segment for this type. Use MapNamespace<" + typeof(T).Name + ">(string name, ...) with an explicit segment, or ensure the project references Nullean.Argh.Generator.");
		}

		configure(new ArghNamespaceBuilder(CreateChildApp(seg), seg));
		return this;
	}

	/// <summary>Creates an <see cref="ArghApp"/> for a child namespace segment (same path rules as <see cref="MapNamespace"/>).</summary>
	internal ArghApp CreateChildApp(string name)
	{
		var childPath = _commandNamespacePath.Length == 0 ? name : _commandNamespacePath + "/" + name;
		return new ArghApp(childPath);
	}

	/// <summary>
	/// Hoists every public method on <typeparamref name="T"/> as a named command and sets one as the scope's root alias.
	/// Analyzed by the source generator; no-op at runtime.
	/// </summary>
	public ArghApp MapAndRootAlias<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class => this;

	/// <summary>Typed options for the current namespace; <typeparamref name="T"/> must inherit the parent options type (enforced at compile time).</summary>
	public ArghApp UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class => this;

	/// <summary>Runs the source-generated CLI for this application (see <see cref="ArghRuntime.RunAsync"/>).</summary>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);

	/// <summary>Gets the delegate stored for a lambda-registered command by its storage key.</summary>
	public static Delegate? GetRegisteredLambda(string key) =>
		Lambdas.TryGetValue(key, out var h) ? h : null;

	/// <summary>
	/// Returns <see langword="true"/> if <paramref name="args"/> resolve to a built-in intrinsic command
	/// (<c>--help</c>, <c>-h</c>, <c>--version</c>, <c>__schema</c>, <c>__completion</c>, <c>__complete</c>)
	/// or a user-defined command marked <c>[CommandIntrinsic]</c>.
	/// <para>
	/// This is the predicate used by <c>AddArgh</c> to suppress host startup logs for intrinsic invocations.
	/// For the pre-host fast-exit path, use <see cref="TryArghIntrinsicCommand"/>.
	/// </para>
	/// </summary>
	public static bool IsIntrinsicCommand(string[] args) =>
		IsArghBuiltinIntrinsic(args) || ArghRuntime.IsUserIntrinsicCommand(args);

	/// <summary>
	/// Pre-host fast path for built-in intrinsic commands. If <paramref name="args"/> match a built-in
	/// (<c>--help</c>, <c>-h</c>, <c>--version</c>, <c>__schema</c>, <c>__completion</c>, <c>__complete</c>),
	/// the command is executed and the process exits via <see cref="Environment.Exit(int)"/>.
	/// If <paramref name="args"/> do not match, this method returns normally and the caller should continue
	/// building and running the host.
	/// <para>
	/// <b>Expert API.</b> Useful when host startup is expensive and you want zero overhead for built-in
	/// intrinsic commands. Place this call before <c>Host.CreateApplicationBuilder</c>. When omitted,
	/// <c>AddArgh</c> still suppresses startup logs for intrinsic commands automatically.
	/// </para>
	/// </summary>
	/// <example>
	/// <code>
	/// await ArghApp.TryArghIntrinsicCommand(args); // exits here for --help, --version, __schema, etc.
	/// var builder = Host.CreateApplicationBuilder(args);
	/// builder.Services.AddArgh(args, b => { b.Map&lt;MyCommands&gt;(); });
	/// await builder.Build().RunAsync();
	/// </code>
	/// </example>
	public static async Task TryArghIntrinsicCommand(string[] args)
	{
		if (!IsArghBuiltinIntrinsic(args)) return;
		var code = await ArghRuntime.RunAsync(args).ConfigureAwait(false);
		Environment.Exit(code);
	}

	private static bool IsArghBuiltinIntrinsic(string[] args) =>
		args is { Length: > 0 } &&
		(CompletionProtocol.IsArghMetaCompletionInvocation(args) ||
		 Array.Exists(args, static a => a is "--help" or "-h" or "--version"));
}
