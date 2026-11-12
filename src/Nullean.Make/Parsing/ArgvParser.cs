using System.Reflection;
using Nullean.Argh.Matching;
using Nullean.Make.Discovery;

namespace Nullean.Make.Parsing;

internal sealed class ParsedArgs
{
	public TargetNode? Target { get; set; }
	public string[] TargetArgs { get; set; } = [];
	public Dictionary<string, string> GlobalValues { get; } = new(StringComparer.OrdinalIgnoreCase);
	public bool SingleTarget { get; set; }
	public bool ShowHelp { get; set; }
	public bool ShowVersion { get; set; }

	/// <summary>Set by <c>--graph</c>: draw the plan in this format and run nothing.</summary>
	public Execution.GraphFormat? Graph { get; set; }
	public string? HelpTarget { get; set; }
}

internal static class ArgvParser
{
	public static ParsedArgs Parse(string[] args, BuildGraph graph, MakeBuild build)
	{
		var result = new ParsedArgs();

		// Extract known global flags/options first so they don't interfere with route resolution
		var remaining = ExtractGlobals(args, graph, build, result);

		if (result.ShowHelp && result.HelpTarget is null && remaining.Count == 0)
			return result;

		// Resolve route: walk non-flag tokens
		var routeTokens = new List<string>();
		var rest = new List<string>();
		var routeDone = false;

		foreach (var token in remaining)
		{
			if (!routeDone && !token.StartsWith("-"))
			{
				var candidate = string.Join("/", [.. routeTokens, token.ToLowerInvariant()]);
				if (graph.ByRoute.ContainsKey(candidate))
				{
					routeTokens.Add(token.ToLowerInvariant());
					continue;
				}
				// Check if it's a known prefix namespace
				var isPrefix = graph.ByRoute.Keys.Any(k => k.StartsWith(candidate + "/", StringComparison.OrdinalIgnoreCase));
				if (isPrefix)
				{
					routeTokens.Add(token.ToLowerInvariant());
					continue;
				}
				routeDone = true;
			}
			rest.Add(token);
		}

		var routeKey = string.Join("/", routeTokens);

		if (result.ShowHelp)
		{
			result.HelpTarget = routeKey;
			return result;
		}

		if (routeTokens.Count == 0)
		{
			// If there's an unrecognized non-flag first token, report it as unknown
			var firstNonFlag = remaining.FirstOrDefault(t => !t.StartsWith("-"));
			if (firstNonFlag is not null)
			{
				var allLeaves = graph.ByRoute.Keys.Select(k => k.Split('/')[^1]).Distinct();
				var suggestions = FuzzyMatch.FindClosest(firstNonFlag.ToLowerInvariant(), allLeaves, 3);
				var hint = suggestions.Count > 0 ? $" Did you mean '{suggestions[0]}'?" : "";
				throw new MakeException($"Unknown target '{firstNonFlag}'.{hint}", 2);
			}
			return result; // show root help
		}

		if (!graph.ByRoute.TryGetValue(routeKey, out var node))
		{
			// Levenshtein suggestion for the last segment
			var last = routeTokens[^1];
			var candidates = graph.ByRoute.Keys.Select(k => k.Split('/')[^1]).Distinct();
			var suggestions = FuzzyMatch.FindClosest(last, candidates, 3);
			var hint = suggestions.Count > 0 ? $" Did you mean '{suggestions[0]}'?" : "";
			throw new MakeException($"Unknown target '{routeKey}'.{hint}", 2);
		}

		result.Target = node;
		result.TargetArgs = rest.ToArray();
		return result;
	}

	private static List<string> ExtractGlobals(string[] args, BuildGraph graph, MakeBuild build, ParsedArgs result)
	{
		var remaining = new List<string>();
		var i = 0;
		while (i < args.Length)
		{
			var arg = args[i];

			if (arg is "-h" or "--help")
			{
				result.ShowHelp = true;
				i++;
				continue;
			}
			if (arg is "--version")
			{
				result.ShowVersion = true;
				i++;
				continue;
			}
			if (arg is "-s" or "--single-target")
			{
				result.SingleTarget = true;
				i++;
				continue;
			}
			if (Execution.GraphOption.TryRead(args, ref i) is { } graphFormat)
			{
				result.Graph = graphFormat;
				continue;
			}

			// Global options
			var matched = false;
			foreach (var opt in graph.GlobalOptions)
			{
				var longNorm = opt.Long.TrimStart('-');
				var shortNorm = opt.Short?.TrimStart('-');

				if (arg.TrimStart('-') == longNorm || (shortNorm is not null && arg.TrimStart('-') == shortNorm))
				{
					if (opt.IsFlag)
					{
						result.GlobalValues[longNorm] = "true";
						SetProperty(opt.Property, build, "true", opt.Long);
					}
					else
					{
						i++;
						if (i >= args.Length)
							throw new MakeException($"Missing value for '{opt.Long}'.", 2);
						result.GlobalValues[longNorm] = args[i];
						SetProperty(opt.Property, build, args[i], opt.Long);
					}
					matched = true;
					break;
				}
			}

			if (!matched)
				remaining.Add(arg);

			i++;
		}
		return remaining;
	}

	private static void SetProperty(PropertyInfo? prop, MakeBuild build, string raw, string flagName)
	{
		if (prop is null)
			return;
		var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
		object value;
		try
		{
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			if (targetType == typeof(bool))
				value = bool.Parse(raw);
			else if (targetType == typeof(int))
				value = int.Parse(raw, inv);
			else if (targetType == typeof(string))
				value = raw;
			else if (targetType.IsEnum)
				value = Enum.Parse(targetType, raw, ignoreCase: true);
			else
				value = Convert.ChangeType(raw, targetType, inv);
		}
		catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or InvalidCastException)
		{
			throw new MakeException($"Cannot parse '{raw}' for '{flagName}'.", 2);
		}
		prop.SetValue(build, value);
	}
}
