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
		spec.Add(StageKind.Requires, builder.Deps.Select(d => new DepRef(resolve(node, d.TypeName), d.Value)).ToList());
		spec.Add(StageKind.Composes, builder.Comps.Select(d => new DepRef(resolve(node, d.TypeName), d.Value)).ToList());
		if (builder.SyncBody is not null || builder.AsyncBody is not null)
			spec.Body = _ => builder.ExecuteAsync();
		return spec;
	}

	public string? FormatValue(TargetNode node, object? value) => ValueDisplay.Differences(value, DefaultValue(node));

	private static object? Leaf(TUnion value) => UnionCaseTable<TUnion>.LeafValue?.Invoke(value);
}
