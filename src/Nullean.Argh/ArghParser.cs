namespace Nullean.Argh;

/// <summary>
/// High-level parse/bind entry points. Routing delegates to source-generated <c>ArghGenerated</c>; DTO binding is not provided for arbitrary types.
/// </summary>
public static class ArghParser
{
	/// <summary>
	/// Routes a command line string to a registered command without invoking handlers. Uses the same splitting rules as <see cref="ArghCli.SplitCommandLine"/> and the same routing rules as <c>ArghGenerated.RunAsync</c>, then delegates to generated <c>ArghGenerated.Route</c> in the application assembly.
	/// </summary>
	/// <returns>The matched command path and remaining arguments, or <see langword="null"/> when no command is matched.</returns>
	public static RouteMatch? Route(string commandLine)
	{
		if (commandLine is null)
			throw new ArgumentNullException(nameof(commandLine));
		return ArghGeneratedRouteInvoker.Route(commandLine);
	}

	/// <summary>
	/// Parses CLI arguments and binds them to <typeparamref name="T"/>. Not supported: the generator does not emit standalone DTO parsers; use command handlers and tests that call handler code directly.
	/// </summary>
	/// <typeparam name="T">CLI options or command DTO type.</typeparam>
	/// <param name="args">Full command line as a single string (shell-style splitting is not performed here).</param>
	/// <exception cref="NotSupportedException">Always thrown.</exception>
	public static T Bind<T>(string args)
	{
		if (args is null)
			throw new ArgumentNullException(nameof(args));
		throw new NotSupportedException(
			"ArghParser.Bind<T> is not supported. The source generator does not emit standalone parsers for arbitrary types '" + typeof(T).FullName + "'. Use ArghGenerated.RunAsync, ArghGenerated.TryParseRoute, or invoke your command handler from tests.");
	}

	/// <summary>
	/// Parses CLI arguments from a character span and binds them to <typeparamref name="T"/>. Not supported for the same reasons as <see cref="Bind{T}(string)"/>.
	/// </summary>
	/// <typeparam name="T">CLI options or command DTO type.</typeparam>
	/// <param name="args">Raw command-line text.</param>
	/// <exception cref="NotSupportedException">Always thrown.</exception>
	public static T Bind<T>(ReadOnlySpan<char> args)
	{
		throw new NotSupportedException(
			"ArghParser.Bind<T> is not supported. The source generator does not emit standalone parsers for arbitrary types '" + typeof(T).FullName + "'. Use ArghGenerated.RunAsync, ArghGenerated.TryParseRoute, or invoke your command handler from tests.");
	}
}
