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
			// `build lint test --filter Unit pack` names three targets; each binds its own arguments.
			var targets = Invocation.Split(root, parsed.TargetArgs, graph);
			parsed.TargetArgs = targets[0].Args;
			var roots = targets.Select(t => new DepRef(t.Node, source.BindArguments(t.Node, t.Args))).ToList();
			var skip = Invocation.ResolveSkips(parsed.Skip, graph);
			plan = RunPlan.Build(roots, source, new PlanOptions(parsed.SingleTarget, EvaluateConditions: parsed.Graph is not null, Skip: skip));
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
			// --skip is ignored here: the whole graph shows every target.
			PlanRenderer.Write(plan, format, Console.Out);
			return 0;
		}
		catch (MakeException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return ex.ExitCode;
		}
	}

	/// <summary>
	/// Runs the plan. An instance starts once everything it waits for has finished, and at most <c>--jobs</c> run at
	/// once, the earliest in the plan first. With one job, or a plan without parallel stages, that is the plan's order.
	/// The first failure cancels the targets still running and starts no more, unless <c>--keep-going</c> lets the
	/// targets that do not depend on it finish.
	/// </summary>
	public static async Task<int> ExecuteAsync(RunPlan plan, ParsedArgs parsed)
	{
		var context = new MakeContext(parsed.GlobalValues, parsed.SingleTarget) { TargetArgs = parsed.TargetArgs };

		// Fail before anything runs, rather than at the target that needs what is missing.
		var missing = plan.MissingPreconditions();
		if (missing.Count > 0)
		{
			Console.Error.WriteLine("[make] The run cannot start. It needs:");
			foreach (var (instance, what) in missing)
				Console.Error.WriteLine($"  {what}, for {instance.Name}");
			return 1;
		}

		// A plan without a parallel stage runs one target at a time, in its order, whatever --jobs says.
		var concurrent = CanRunSideBySide(plan) && (parsed.Jobs ?? Environment.ProcessorCount) > 1;
		var jobs = concurrent ? parsed.Jobs ?? Environment.ProcessorCount : 1;

		using var cancellation = new CancellationTokenSource();
		var stdout = Console.Out;
		var stderr = Console.Error;
		var gate = new object();
		var mode = parsed.Output ?? OutputMode.Prefixed;
		if (concurrent)
		{
			Console.SetOut(new OutputRouter(stdout, error: false));
			Console.SetError(new OutputRouter(stderr, error: true));
		}

		var done = new HashSet<TargetInstance>();
		var blocked = new HashSet<TargetInstance>();
		var running = new Dictionary<Task<(int ExitCode, TimeSpan Elapsed, string? Message)>, TargetInstance>();
		var pending = plan.Instances.ToList();
		var failures = new List<(TargetInstance Step, int ExitCode)>();
		var started = DateTime.UtcNow;
		var timings = new List<(TargetInstance Step, TimeSpan Elapsed)>();
		var startOrder = new List<TargetInstance>();

		try
		{
			while (pending.Count > 0 || running.Count > 0)
			{
				var stopping = failures.Count > 0 && !parsed.KeepGoing;
				while (!stopping && running.Count < jobs)
				{
					// A target that waits for one that failed, or for one that could not run, cannot run either.
					foreach (var waiting in pending.Where(p => p.Prerequisites.Any(blocked.Contains)).ToList())
					{
						blocked.Add(waiting);
						pending.Remove(waiting);
					}
					var next = pending.FirstOrDefault(p => p.Prerequisites.All(done.Contains));
					if (next is null)
						break;
					pending.Remove(next);

					// A named command that only sequences other targets has nothing of its own to run.
					if (next.IsRoot && next.Spec.Body is null)
					{
						done.Add(next);
						continue;
					}

					startOrder.Add(next);
					var sink = concurrent ? new TargetSink(next.Name, mode, stdout, stderr, gate) : null;
					lock (gate)
						ExecutionLog.Starting(next, stdout);
					running[RunStep(next, context.ForTarget(cancellation.Token, sink), sink)] = next;
				}

				if (running.Count == 0)
					break;

				var finished = await Task.WhenAny(running.Keys);
				var step = running[finished];
				running.Remove(finished);
				var (exitCode, elapsed, message) = await finished;
				if (exitCode == 0)
				{
					done.Add(step);
					timings.Add((step, elapsed));
					lock (gate)
						ExecutionLog.Finished(step, elapsed, stdout);
					continue;
				}

				blocked.Add(step);
				if (exitCode < 0)
					continue; // cancelled because another target failed

				failures.Add((step, exitCode));
				lock (gate)
					stderr.WriteLine($"[make] Target '{string.Join("/", step.Node.Route)}' failed: {message}");
				if (!parsed.KeepGoing)
					await cancellation.CancelAsync();
			}
		}
		finally
		{
			if (concurrent)
			{
				Console.SetOut(stdout);
				Console.SetError(stderr);
			}
		}

		var cleanupExit = await RunCleanups(plan, startOrder, context);

		if (concurrent)
			ExecutionLog.Summary(timings, DateTime.UtcNow - started, failures.Select(f => f.Step).ToList(), stdout);
		return failures.Count > 0 ? failures[0].ExitCode : cleanupExit;
	}

	/// <summary>
	/// Runs the cleanup targets of every instance that started, newest first, each once, whether the run failed or not.
	/// A cleanup runs its body only. Returns the exit code of the first cleanup that failed, or 0.
	/// </summary>
	private static async Task<int> RunCleanups(RunPlan plan, List<TargetInstance> startOrder, MakeContext context)
	{
		var exitCode = 0;
		var ran = new HashSet<TargetInstance>();
		for (var i = startOrder.Count - 1; i >= 0; i--)
		{
			if (!plan.Cleanups.TryGetValue(startOrder[i], out var cleanups))
				continue;
			foreach (var cleanup in cleanups.Where(ran.Add))
			{
				ExecutionLog.Starting(cleanup, Console.Out);
				var (code, elapsed, message) = await RunStep(cleanup, context.ForTarget(CancellationToken.None, null), null);
				if (code == 0)
				{
					ExecutionLog.Finished(cleanup, elapsed, Console.Out);
					continue;
				}
				Console.Error.WriteLine($"[make] Cleanup '{string.Join("/", cleanup.Node.Route)}' failed: {message}");
				if (exitCode == 0)
					exitCode = code;
			}
		}
		return exitCode;
	}

	/// <summary>Whether two targets of the plan can ever be ready at once: a parallel stage, or roots that do not chain.</summary>
	private static bool CanRunSideBySide(RunPlan plan) =>
		plan.Instances.Any(i => i.Stages.Any(s => s.Parallel && s.Items.Count(item => item.Instance is not null) > 1)) || !plan.Chained && plan.Roots.Count > 1;

	/// <summary>
	/// Runs one instance on its own task. Returns 0, the exit code of a failure, or -1 when the run's cancellation
	/// stopped it.
	/// </summary>
	private static Task<(int ExitCode, TimeSpan Elapsed, string? Message)> RunStep(TargetInstance step, MakeContext context, TargetSink? sink) =>
		Task.Run(async () =>
		{
			var start = DateTime.UtcNow;
			MakeContext.Current = context;
			OutputRouter.Current = sink;
			try
			{
				await InvokeStep(step, context.CancellationToken);
				return (0, DateTime.UtcNow - start, (string?)null);
			}
			catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
			{
				return (-1, DateTime.UtcNow - start, null);
			}
			catch (MakeException ex)
			{
				return (ex.ExitCode, DateTime.UtcNow - start, ex.Message);
			}
			catch (Exception ex) when (context.CancellationToken.IsCancellationRequested)
			{
				// A body stopped by the cancellation fails with whatever its command threw; that is not a failure of its own.
				_ = ex;
				return (-1, DateTime.UtcNow - start, null);
			}
			catch (Exception ex)
			{
				return (1, DateTime.UtcNow - start, ex.Message);
			}
			finally
			{
				sink?.Complete();
				OutputRouter.Current = null;
				MakeContext.Current = null;
			}
		});

	private static async Task InvokeStep(TargetInstance step, CancellationToken token)
	{
		if (step.Spec.OnlyWhen is not null && !step.Spec.OnlyWhen())
		{
			ExecutionLog.Skipped(step);
			return;
		}

		if (step.Spec.Body is not null)
			await step.Spec.Body(token);
	}
}
