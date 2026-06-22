using Nullean.Make.Discovery;
using Nullean.Make.Parsing;

namespace Nullean.Make.Execution;

internal static class DepGraphExecutor
{
	public static async Task<int> ExecuteAsync(TargetNode root, ParsedArgs parsed, BuildGraph graph)
	{
		var plan = BuildPlan(root, parsed.SingleTarget);
		var context = new MakeContext(parsed.GlobalValues, parsed.SingleTarget)
		{
			TargetArgs = parsed.TargetArgs
		};

		foreach (var step in plan)
		{
			var start = DateTime.UtcNow;
			ExecutionLog.Starting(step);
			MakeContext.Current = context;
			try
			{
				await InvokeStep(step, parsed.TargetArgs);
			}
			catch (MakeException ex)
			{
				Console.Error.WriteLine($"[make] Target '{string.Join("/", step.Route)}' failed: {ex.Message}");
				return ex.ExitCode;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[make] Target '{string.Join("/", step.Route)}' failed: {ex.Message}");
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

	private static async Task InvokeStep(TargetNode step, string[] targetArgs)
	{
		if (step.OnlyWhen is not null && !step.OnlyWhen())
		{
			ExecutionLog.Skipped(step);
			return;
		}

		if (step.DtoType is not null && step.TypedBody is not null)
		{
			var dto = DtoBinder.Bind(step.DtoType, targetArgs);
			// Invoke Action<T> or Func<T, Task>
			var bodyType = step.TypedBody.GetType();
			if (bodyType.IsGenericType)
			{
				var genDef = bodyType.GetGenericTypeDefinition();
				if (genDef == typeof(Func<,>) && bodyType.GetGenericArguments()[1] == typeof(Task))
				{
					var task = (Task)step.TypedBody.DynamicInvoke(dto)!;
					await task;
					return;
				}
			}
			step.TypedBody.DynamicInvoke(dto);
			return;
		}

		if (step.AsyncBody is not null)
		{
			await step.AsyncBody();
			return;
		}

		step.SyncBody?.Invoke();
	}

	private static List<TargetNode> BuildPlan(TargetNode root, bool singleTarget)
	{
		var plan = new List<TargetNode>();
		var visited = new HashSet<TargetNode>();

		void AddNode(TargetNode node, bool isRoot)
		{
			if (!visited.Add(node))
				return;

			// Requires: skippable under -s (unless this is the root node itself)
			if (!singleTarget || !isRoot)
			{
				foreach (var dep in node.RequiresResolved)
					AddNode(dep, false);
			}

			// Composes: always run
			foreach (var dep in node.ComposesResolved)
				AddNode(dep, false);

			plan.Add(node);
		}

		AddNode(root, true);

		// Remove root if it has no body and is a pure command (only composes/requires)
		// Keep it if it has a body
		if (root.SyncBody is null && root.AsyncBody is null && root.TypedBody is null && plan[^1] == root)
			plan.RemoveAt(plan.Count - 1);

		return plan;
	}
}
