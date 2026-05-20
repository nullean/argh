using System.Reflection;

namespace Nullean.Make.Discovery;

internal sealed class BuildGraph
{
	/// <summary>All discovered targets in declaration order.</summary>
	public List<TargetNode> Targets { get; } = new();

	/// <summary>Maps CLI route key ("pkg/generate") to node.</summary>
	public Dictionary<string, TargetNode> ByRoute { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Maps configure-method identity to node (for dep resolution).</summary>
	public Dictionary<MethodInfo, TargetNode> ByMethod { get; } = new();

	/// <summary>Global CLI options declared on the Build class.</summary>
	public List<GlobalOptionNode> GlobalOptions { get; } = new();

	public string AppName { get; set; } = "make";
}
