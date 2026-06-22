using Nullean.Make.Discovery;
using Nullean.Make.Execution;
using Nullean.Make.Help;
using Nullean.Make.Parsing;

namespace Nullean.Make;

/// <summary>Entry point for Make-based build scripts.</summary>
public static class MakeApp
{
	/// <summary>
	/// Discovers all targets on <typeparamref name="TBuild"/>, parses <paramref name="args"/>, and executes the requested target.
	/// Returns an exit code suitable for passing to <c>Environment.Exit</c> or returning from top-level statements.
	/// </summary>
	public static async Task<int> Execute<TBuild>(string[] args) where TBuild : MakeBuild, new()
	{
		TBuild build;
		BuildGraph graph;

		try
		{
			build = new TBuild();
			graph = BuildScanner.Scan(build);
			GraphValidator.Validate(graph);
		}
		catch (MakeException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return ex.ExitCode;
		}

		var scriptName = DetectScriptName();

		if (args.Length == 0)
		{
			MakeHelpPrinter.PrintRoot(graph, scriptName);
			return 0;
		}

		ParsedArgs parsed;
		try
		{
			parsed = ArgvParser.Parse(args, graph, build);
		}
		catch (MakeException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return ex.ExitCode;
		}

		if (parsed.ShowVersion)
		{
			var asm = typeof(TBuild).Assembly;
			var ver = asm.GetName().Version?.ToString() ?? "0.0.0";
			Console.WriteLine(ver);
			return 0;
		}

		if (parsed.ShowHelp)
		{
			if (string.IsNullOrEmpty(parsed.HelpTarget) || !graph.ByRoute.TryGetValue(parsed.HelpTarget, out var helpNode))
				MakeHelpPrinter.PrintRoot(graph, scriptName);
			else if (helpNode.Kind == TargetKind.Command)
				MakeHelpPrinter.PrintCommand(helpNode, graph, scriptName);
			else
				MakeHelpPrinter.PrintTarget(helpNode, graph, scriptName);
			return 0;
		}

		if (parsed.Target is null)
		{
			MakeHelpPrinter.PrintRoot(graph, scriptName);
			return 0;
		}

		return await DepGraphExecutor.ExecuteAsync(parsed.Target, parsed, graph);
	}

	private static string DetectScriptName()
	{
		var entry = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "make";
		return entry;
	}
}
