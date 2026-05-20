namespace Nullean.Make.Discovery;

internal static class GraphValidator
{
	/// <summary>Detects cycles in the dep graph. Throws <see cref="MakeException"/> with the offending path if a cycle is found.</summary>
	public static void Validate(BuildGraph graph)
	{
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var node in graph.Targets)
		{
			if (!visited.Contains(node.Name))
				Visit(node, visited, stack, []);
		}
	}

	private static void Visit(TargetNode node, HashSet<string> visited, HashSet<string> stack, List<string> path)
	{
		var key = string.Join("/", node.Route);
		if (stack.Contains(key))
			throw new MakeException($"Dependency cycle detected: {string.Join(" → ", path)} → {key}");

		if (visited.Contains(key))
			return;

		stack.Add(key);
		path.Add(key);

		foreach (var dep in node.RequiresResolved)
			Visit(dep, visited, stack, path);
		foreach (var dep in node.ComposesResolved)
			Visit(dep, visited, stack, path);

		path.RemoveAt(path.Count - 1);
		stack.Remove(key);
		visited.Add(key);
	}
}
