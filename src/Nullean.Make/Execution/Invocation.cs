using Nullean.Argh.Matching;
using Nullean.Make.Discovery;

namespace Nullean.Make.Execution;

/// <summary>Splits a command line that names several targets, such as <c>build lint test --filter Unit pack</c>.</summary>
internal static class Invocation
{
	/// <summary>
	/// Splits the tokens after the first target's route into that target's arguments and the targets after it. A token
	/// starts the next target when it names one, is not the value of the option before it, and the current target takes
	/// no positional arguments. Everything after <c>--</c> stays with the current target.
	/// </summary>
	public static List<(TargetNode Node, string[] Args)> Split(TargetNode first, string[] args, BuildGraph graph)
	{
		var result = new List<(TargetNode Node, string[] Args)>();
		var node = first;
		var current = new List<string>();
		var options = Options(node);
		for (var i = 0; i < args.Length; i++)
		{
			var token = args[i];
			if (token == "--")
			{
				current.AddRange(args[i..]);
				break;
			}
			if (token.StartsWith('-'))
			{
				current.Add(token);
				// The value of an option that takes one stays with it, even when it names a target.
				if (!token.Contains('=') && options.TakesValue(token) && i + 1 < args.Length)
					current.Add(args[++i]);
				continue;
			}
			if (!options.TakesPositionals && MatchRoute(args, i, graph) is { } next)
			{
				result.Add((node, [.. current]));
				node = next.Node;
				options = Options(node);
				current = new List<string>();
				i += next.Consumed - 1;
				continue;
			}
			current.Add(token);
		}
		result.Add((node, [.. current]));
		return result;
	}

	/// <summary>The longest route starting at <c>args[start]</c>, such as <c>pkg generate</c>, and how many tokens it took.</summary>
	private static (TargetNode Node, int Consumed)? MatchRoute(string[] args, int start, BuildGraph graph)
	{
		(TargetNode, int)? best = null;
		var segments = new List<string>();
		for (var i = start; i < args.Length && !args[i].StartsWith('-'); i++)
		{
			segments.Add(args[i].ToLowerInvariant());
			var key = string.Join("/", segments);
			if (graph.ByRoute.TryGetValue(key, out var node))
				best = (node, segments.Count);
			else if (!graph.ByRoute.Keys.Any(k => k.StartsWith(key + "/", StringComparison.OrdinalIgnoreCase)))
				break;
		}
		return best;
	}

	private sealed record TargetOptions(HashSet<string> ValueOptions, bool TakesPositionals)
	{
		public bool TakesValue(string token) => ValueOptions.Contains(token.TrimStart('-'));
	}

	private static TargetOptions Options(TargetNode node)
	{
		if (node.DtoType is null)
			return new TargetOptions(new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
		var described = DtoOptions.Describe(node.DtoType);
		return new TargetOptions(
			described
				.Where(o => !o.IsPositional && !o.IsRest && !o.IsSwitch && !IsFSharpBoolOption(o.Type))
				.Select(o => o.Name)
				.ToHashSet(StringComparer.OrdinalIgnoreCase),
			described.Any(o => o.IsPositional)
		);
	}

	private static bool IsFSharpBoolOption(Type type) =>
		type.IsGenericType && type.GetGenericTypeDefinition().Name == "FSharpOption`1" && type.GetGenericArguments()[0] == typeof(bool);

	/// <summary>Resolves the targets of <c>--skip</c>, given as <c>pkg generate</c>, <c>pkg/generate</c> or <c>pkg:generate</c>.</summary>
	public static HashSet<TargetNode> ResolveSkips(IEnumerable<string> names, BuildGraph graph)
	{
		var skipped = new HashSet<TargetNode>();
		foreach (var name in names)
		{
			var key = string.Join("/", name.Split([' ', '/', ':'], StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
			if (graph.ByRoute.TryGetValue(key, out var node))
			{
				skipped.Add(node);
				continue;
			}
			var suggestions = FuzzyMatch.FindClosest(key, graph.ByRoute.Keys.Select(k => k.Replace('/', ' ')), 3);
			var hint = suggestions.Count > 0 ? $" Did you mean '{suggestions[0]}'?" : "";
			throw new MakeException($"Unknown target '{name}' for '--skip'.{hint}", 2);
		}
		return skipped;
	}
}
