using Nullean.Make.Discovery;
using Nullean.Make.Execution;
using Nullean.Make.Parsing;

namespace Nullean.Make.UnionGraph;

/// <summary>
/// The targets of a <see cref="MakeApp{TUnion}"/>: a node's value is its case record. <c>Bind</c> runs again for each
/// value, so a dependency declared as <c>new Test("Unit")</c> runs the body <c>Bind</c> returns for that case.
/// </summary>
internal sealed class UnionTargetSource<TUnion>(
	Func<TUnion, IUnionTargetBuilder<TUnion>> bind,
	IReadOnlyDictionary<TargetNode, UnionCase<TUnion>> cases,
	Func<TargetNode, string, TargetNode> resolve
) : ITargetSource where TUnion : struct
{
	public object? DefaultValue(TargetNode node) => Leaf(cases[node].CreateDefault());

	public object? BindArguments(TargetNode node, string[] args) => DtoBinder.Bind(cases[node].CaseType, args);

	public InstanceSpec Describe(TargetNode node, object? value)
	{
		var builder = (UnionTargetBuilderImpl<TUnion>)bind(value is null ? cases[node].CreateDefault() : cases[node].Wrap(value));
		var spec = new InstanceSpec();
		// Requires are gates: they run before the composed stages, whatever order they were declared in.
		foreach (var (kind, parallel, items) in builder.Stages.OrderBy(s => s.Kind))
			spec.Add(kind, items.Select(d => new DepRef(resolve(node, d.TypeName), d.Value)).ToList(), parallel);
		if (builder.SyncBody is not null || builder.AsyncBody is not null)
			spec.Body = builder.ExecuteAsync;
		spec.OnlyWhen = builder.Condition;
		// A case named with its default value in After matches the target with any value.
		foreach (var d in builder.AfterRefs)
		{
			var target = resolve(node, d.TypeName);
			spec.After.Add(new DepRef(target, Equals(d.Value, DefaultValue(target)) ? null : d.Value));
		}
		spec.Cleanup.AddRange(builder.CleanupRefs.Select(d => new DepRef(resolve(node, d.TypeName), d.Value)));
		spec.NeedsEnv.AddRange(builder.Env);
		spec.NeedsTool.AddRange(builder.Tools);
		return spec;
	}

	public string? FormatValue(TargetNode node, object? value) => ValueDisplay.Differences(value, DefaultValue(node));

	private static object? Leaf(TUnion value) => UnionCaseTable<TUnion>.LeafValue?.Invoke(value);
}
