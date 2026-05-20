using Nullean.Make.Discovery;
using Nullean.Argh.Help;

namespace Nullean.Make.Execution;

internal static class ExecutionLog
{
	public static void Starting(TargetNode node)
	{
		var name = CliHelpFormatting.Accent(string.Join(" ", node.Route));
		Console.WriteLine($"Starting  {name}");
	}

	public static void Finished(TargetNode node, TimeSpan elapsed)
	{
		var name = CliHelpFormatting.Accent(string.Join(" ", node.Route));
		Console.WriteLine($"Finished  {name} ({elapsed.TotalSeconds:F1}s)");
	}

	public static void Skipped(TargetNode node)
	{
		var name = CliHelpFormatting.Placeholder(string.Join(" ", node.Route));
		Console.WriteLine($"Skipped   {name}");
	}
}
