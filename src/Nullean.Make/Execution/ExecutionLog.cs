using Nullean.Argh.Help;

namespace Nullean.Make.Execution;

internal static class ExecutionLog
{
	public static void Starting(TargetInstance step) => Console.WriteLine($"Starting  {CliHelpFormatting.Accent(step.Name)}");

	public static void Finished(TargetInstance step, TimeSpan elapsed) =>
		Console.WriteLine($"Finished  {CliHelpFormatting.Accent(step.Name)} ({elapsed.TotalSeconds:F1}s)");

	public static void Skipped(TargetInstance step) => Console.WriteLine($"Skipped   {CliHelpFormatting.Placeholder(step.Name)}");
}
