namespace Nullean.Argh;

/// <summary>
/// Routing helpers that delegate to source-generated code in the application assembly.
/// </summary>
/// <remarks>
/// <para>
/// Parameter binding for global options, group options, and <c>[AsParameters]</c> types is implemented as
/// <b>pregenerated C#</b> in <c>ArghGenerated</c> (no reflection, AOT-safe), following the same idea as
/// <see href="https://github.com/Cysharp/ConsoleAppFramework/pull/237">ConsoleAppFramework PR #237</see>:
/// the generator emits parsers and object construction (<c>new T(...)</c>) for each registered shape.
/// </para>
/// <para>
/// There is no generic <c>Bind&lt;T&gt;()</c> that parses arbitrary <typeparamref name="T"/> at runtime; that would
/// require reflection or a non–AOT-safe registry. Tests should call generated entry points, use
/// <see cref="ArghCli.RunWithCaptureAsync(System.Func{System.Threading.Tasks.Task{int}})"/>, or invoke handlers/DTO constructors directly.
/// </para>
/// </remarks>
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
}
