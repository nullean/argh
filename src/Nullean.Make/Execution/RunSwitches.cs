using System.Globalization;
using Nullean.Make.Parsing;

namespace Nullean.Make.Execution;

/// <summary>
/// The switches every Make entry point reads besides <c>-s</c>, <c>--help</c> and <c>--version</c>: <c>--graph</c>,
/// <c>-j</c> / <c>--jobs</c>, <c>-k</c> / <c>--keep-going</c> and <c>--output</c>. A global option the build declares
/// under the same name wins, so adding these breaks no script that already uses one of them.
/// </summary>
internal static class RunSwitches
{
	/// <summary>Reads the switch at <c>args[i]</c> into <paramref name="parsed"/> and advances <paramref name="i"/> past it.</summary>
	/// <returns><see langword="false"/> when <c>args[i]</c> is not one of the switches.</returns>
	public static bool TryRead(IReadOnlyList<string> args, ref int i, ParsedArgs parsed, Func<string, bool> declaredByBuild)
	{
		var arg = args[i];
		var name = arg.Split('=', 2)[0];
		if (declaredByBuild(name))
			return false;

		if (GraphOption.TryRead(args, ref i) is { } format)
		{
			parsed.Graph = format;
			return true;
		}

		switch (name)
		{
			case "-j" or "--jobs":
				parsed.Jobs = ParseJobs(Value(args, ref i, name));
				return true;
			case "-k" or "--keep-going":
				parsed.KeepGoing = true;
				i++;
				return true;
			case "--output":
				parsed.Output = Value(args, ref i, name).ToLowerInvariant() switch
				{
					"prefixed" => OutputMode.Prefixed,
					"grouped" => OutputMode.Grouped,
					var other => throw new MakeException($"Unknown output mode '{other}' for '--output'. Known: prefixed, grouped.", 2)
				};
				return true;
		}
		return false;
	}

	/// <summary>The value of <c>--name=value</c> or <c>--name value</c>.</summary>
	private static string Value(IReadOnlyList<string> args, ref int i, string name)
	{
		var arg = args[i++];
		var eq = arg.IndexOf('=');
		if (eq > 0)
			return arg[(eq + 1)..];
		if (i >= args.Count)
			throw new MakeException($"Missing value for '{name}'.", 2);
		return args[i++];
	}

	private static int ParseJobs(string raw) =>
		int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var jobs) && jobs > 0
			? jobs
			: throw new MakeException($"'--jobs' takes a number of targets greater than 0, not '{raw}'.", 2);
}
