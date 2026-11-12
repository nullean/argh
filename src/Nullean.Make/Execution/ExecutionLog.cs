using Nullean.Argh.Help;

namespace Nullean.Make.Execution;

internal static class ExecutionLog
{
	public static void Starting(TargetInstance step, TextWriter output) => output.WriteLine($"Starting  {CliHelpFormatting.Accent(step.Name)}");

	public static void Finished(TargetInstance step, TimeSpan elapsed, TextWriter output) =>
		output.WriteLine($"Finished  {CliHelpFormatting.Accent(step.Name)} ({elapsed.TotalSeconds:F1}s)");

	public static void Skipped(TargetInstance step) => Console.WriteLine($"Skipped   {CliHelpFormatting.Placeholder(step.Name)}");

	/// <summary>
	/// After a run in which targets ran side by side: each target's time, and the wall-clock time against their sum,
	/// which is what running them side by side saved.
	/// </summary>
	public static void Summary(
		IReadOnlyList<(TargetInstance Step, TimeSpan Elapsed)> timings,
		TimeSpan wall,
		IReadOnlyList<TargetInstance> failed,
		TextWriter output
	)
	{
		if (timings.Count + failed.Count == 0)
			return;
		var width = timings.Select(t => t.Step.Name.Length).Concat(failed.Select(f => f.Name.Length)).Max() + 2;
		output.WriteLine();
		foreach (var (step, elapsed) in timings)
			output.WriteLine($"  {step.Name.PadRight(width)}{elapsed.TotalSeconds,7:F1}s");
		foreach (var step in failed)
			output.WriteLine($"  {step.Name.PadRight(width)} failed");
		var sum = timings.Aggregate(TimeSpan.Zero, (total, t) => total + t.Elapsed);
		output.WriteLine($"  {CliHelpFormatting.Section("Took")} {wall.TotalSeconds:F1}s for {sum.TotalSeconds:F1}s of work");
	}
}
