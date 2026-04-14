namespace Nullean.Argh.Runtime;

/// <summary>
/// Holds delegates to the application assembly's source-generated <see cref="Nullean.Argh.ArghGenerated"/> entry points,
/// registered by a module initializer emitted with the generator (AOT-safe, no reflection).
/// </summary>
public static class ArghRuntime
{
	private static Func<string[], Task<int>>? _runAsyncFunc;
	private static Func<string[], RouteMatch?>? _routeFunc;

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
	/// Runs the source-generated CLI for the application assembly (same behavior as <c>ArghGenerated.RunAsync</c>).
	/// </summary>
	public static Task<int> RunAsync(string[] args)
	{
		if (_runAsyncFunc is null)
			throw new InvalidOperationException(
				"CLI runner is not registered. Reference Nullean.Argh, register commands with ArghApp, and ensure the source generator runs so ArghGenerated is emitted in this assembly.");

		return _runAsyncFunc(args);
	}

	/// <summary>
	/// Routes argv using the same rules as <see cref="RunAsync"/> without invoking handlers.
	/// </summary>
	public static RouteMatch? Route(string[] args)
	{
		if (_routeFunc is null)
			throw new InvalidOperationException(
				"CLI route delegate is not registered. Reference Nullean.Argh, register commands with ArghApp, and ensure the source generator runs so ArghGenerated is emitted in this assembly.");

		if (args is null)
			throw new ArgumentNullException(nameof(args));

		return _routeFunc(args);
	}
}
