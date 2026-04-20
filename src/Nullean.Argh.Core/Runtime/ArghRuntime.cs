using System.Text.Json;
using Nullean.Argh.Schema;

namespace Nullean.Argh.Runtime;

/// <summary>
/// Holds delegates to the application assembly's source-generated CLI entry type in the <c>Nullean.Argh</c> namespace (name is derived per assembly),
/// registered by a module initializer emitted with the generator (AOT-safe, no reflection).
/// </summary>
public static class ArghRuntime
{
	private static Func<string[], Task<int>>? _runAsyncFunc;
	private static Func<string[], RouteMatch?>? _routeFunc;
	private static Func<ArghCliSchemaDocument>? _cliSchemaFactory;

	/// <summary>
	/// Registers the generated CLI runner. Called from emitted module initialization; not intended for app code.
	/// </summary>
	public static void RegisterRunner(Func<string[], Task<int>> runAsync) =>
		_runAsyncFunc = runAsync ?? throw new ArgumentNullException(nameof(runAsync));

	/// <summary>
	/// Registers the generated route helper. Called from emitted module initialization; not intended for app code.
	/// </summary>
	public static void RegisterRoute(Func<string[], RouteMatch?> route) =>
		_routeFunc = route ?? throw new ArgumentNullException(nameof(route));

	/// <summary>
	/// Registers the generated CLI schema document factory. Called from emitted module initialization; not intended for app code.
	/// </summary>
	public static void RegisterCliSchema(Func<ArghCliSchemaDocument> factory) =>
		_cliSchemaFactory = factory ?? throw new ArgumentNullException(nameof(factory));

	/// <summary>
	/// Serializes the registered <see cref="ArghCliSchemaDocument"/> to indented JSON (camelCase), for <c>__schema</c> and programmatic use.
	/// </summary>
	public static string FormatCliSchemaJson()
	{
		if (_cliSchemaFactory is null)
			throw new InvalidOperationException(
				"CLI schema factory is not registered. Reference Nullean.Argh, register commands with ArghApp, and ensure the source generator runs so generated entry types are emitted in this assembly.");

		return JsonSerializer.Serialize(_cliSchemaFactory(), ArghCliSchemaJsonContext.Default.ArghCliSchemaDocument);
	}

	/// <summary>
	/// Runs the source-generated CLI for the application assembly (same behavior as the generated <c>RunAsync</c> on the per-assembly CLI entry type).
	/// </summary>
	public static Task<int> RunAsync(string[] args)
	{
		if (_runAsyncFunc is null)
			throw new InvalidOperationException(
				"CLI runner is not registered. Reference Nullean.Argh, register commands with ArghApp, and ensure the source generator runs so generated entry types are emitted in this assembly.");

		return _runAsyncFunc(args);
	}

	/// <summary>
	/// Routes argv using the same rules as <see cref="RunAsync"/> without invoking handlers.
	/// </summary>
	public static RouteMatch? Route(string[] args)
	{
		if (_routeFunc is null)
			throw new InvalidOperationException(
				"CLI route delegate is not registered. Reference Nullean.Argh, register commands with ArghApp, and ensure the source generator runs so generated entry types are emitted in this assembly.");

		if (args is null)
			throw new ArgumentNullException(nameof(args));

		return _routeFunc(args);
	}
}
