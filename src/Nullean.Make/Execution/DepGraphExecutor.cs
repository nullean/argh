using Nullean.Make.Discovery;
using Nullean.Make.Parsing;

namespace Nullean.Make.Execution;

internal static class DepGraphExecutor
{
	/// <summary>
	/// Binds the named target's arguments, plans the run and executes it. With <c>--graph</c>, draws the plan instead
	/// and runs nothing.
	/// </summary>
	public static async Task<int> ExecuteAsync(TargetNode root, ParsedArgs parsed, BuildGraph graph)
	{
		var source = graph.Source ?? throw new InvalidOperationException("The graph has no target source.");
		RunPlan plan;
		try
		{
			var value = source.BindArguments(root, parsed.TargetArgs);
			plan = RunPlan.Build([new DepRef(root, value)], source, new PlanOptions(parsed.SingleTarget, EvaluateConditions: parsed.Graph is not null));
		}
		catch (MakeException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return ex.ExitCode;
		}

		if (parsed.Graph is { } format)
		{
			PlanRenderer.Write(plan, format, Console.Out);
			return 0;
		}
		return await ExecuteAsync(plan, parsed);
	}

	/// <summary><c>--graph</c> without a target: draws every target nothing else depends on.</summary>
	public static int DrawWholeGraph(BuildGraph graph, GraphFormat format)
	{
		var source = graph.Source ?? throw new InvalidOperationException("The graph has no target source.");
		try
		{
			var plan = PlanRenderer.WholeGraph(graph, source, new PlanOptions(SingleTarget: false, EvaluateConditions: true));
			PlanRenderer.Write(plan, format, Console.Out);
			return 0;
		}
		catch (MakeException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return ex.ExitCode;
		}
	}

	public static async Task<int> ExecuteAsync(RunPlan plan, ParsedArgs parsed)
	{
		var context = new MakeContext(parsed.GlobalValues, parsed.SingleTarget) { TargetArgs = parsed.TargetArgs };

		foreach (var step in plan.Instances)
		{
			// A named command that only sequences other targets has nothing of its own to run.
			if (step.IsRoot && step.Spec.Body is null)
				continue;

			var start = DateTime.UtcNow;
			ExecutionLog.Starting(step);
			MakeContext.Current = context;
			try
			{
				await InvokeStep(step);
			}
			catch (MakeException ex)
			{
				Console.Error.WriteLine($"[make] Target '{string.Join("/", step.Node.Route)}' failed: {ex.Message}");
				return ex.ExitCode;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[make] Target '{string.Join("/", step.Node.Route)}' failed: {ex.Message}");
				return 1;
			}
			finally
			{
				MakeContext.Current = null;
			}
			ExecutionLog.Finished(step, DateTime.UtcNow - start);
		}

		return 0;
	}

	private static async Task InvokeStep(TargetInstance step)
	{
		if (step.Spec.OnlyWhen is not null && !step.Spec.OnlyWhen())
		{
			ExecutionLog.Skipped(step);
			return;
		}

		if (step.Spec.Body is not null)
			await step.Spec.Body(CancellationToken.None);
	}
}
