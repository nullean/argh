using Nullean.Make.Execution;
using Nullean.Make.Parsing;

namespace Nullean.Make.Discovery;

/// <summary>The targets of a <see cref="MakeBuild"/> class: a node's value is its DTO, or <c>null</c> when it has none.</summary>
internal sealed class ClassTargetSource(BuildGraph graph) : ITargetSource
{
	public object? DefaultValue(TargetNode node) => node.DtoType is null ? null : DtoBinder.Bind(node.DtoType, []);

	public object? BindArguments(TargetNode node, string[] args) => node.DtoType is null ? null : DtoBinder.Bind(node.DtoType, args);

	public InstanceSpec Describe(TargetNode node, object? value)
	{
		var spec = new InstanceSpec { OnlyWhen = node.OnlyWhen };
		spec.Add(StageKind.Requires, Resolve(node.Requires));
		spec.Add(StageKind.Composes, Resolve(node.Composes));

		if (node.TypedBody is { } typed && node.DtoType is not null)
			spec.Body = _ => InvokeTyped(typed, value);
		else if (node.AsyncBody is { } asyncBody)
			spec.Body = _ => asyncBody();
		else if (node.SyncBody is { } syncBody)
			spec.Body = _ =>
			{
				syncBody();
				return Task.CompletedTask;
			};
		return spec;
	}

	public string? FormatValue(TargetNode node, object? value) => ValueDisplay.Differences(value, DefaultValue(node));

	/// <summary>A reference to a delegate that is not a target of this build resolves to nothing, as it always has.</summary>
	private List<DepRef> Resolve(List<TargetRef> references) =>
		references.Where(r => graph.ByMethod.ContainsKey(r.Method)).Select(r => new DepRef(graph.ByMethod[r.Method], r.Value)).ToList();

	/// <summary>Invokes an <c>Action&lt;T&gt;</c> or a <c>Func&lt;T, Task&gt;</c> body with the bound DTO.</summary>
	private static Task InvokeTyped(Delegate body, object? value)
	{
		try
		{
			return body.DynamicInvoke(value) as Task ?? Task.CompletedTask;
		}
		catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
		{
			// Rethrow what the body threw, so a MakeException keeps its exit code and message.
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
			throw;
		}
	}
}
