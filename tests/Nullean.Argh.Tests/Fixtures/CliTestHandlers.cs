using System.Collections.Generic;
using System.IO;
using Nullean.Argh;

namespace Nullean.Argh.Tests.Fixtures;

internal enum TestColor
{
	/// <summary>The color red.</summary>
	Red,
	/// <summary>The color blue.</summary>
	Blue
}

/// <summary>Sample record bound via <see cref="AsParametersAttribute"/>.</summary>
/// <param name="Env">Deployment environment name.</param>
/// <param name="Port">Listen port.</param>
internal sealed record DeployCliArgs(string Env, int Port);

internal static class CliTestHandlers
{
	/// <summary>Greet someone by name.</summary>
	/// <example>hello --name world</example>
	/// <param name="name">The name to greet.</param>
	[FilterAttribute<TestsPerCommandFilter>]
	public static void Hello(string name) =>
		System.Console.Out.WriteLine($"ok:{name}");

	/// <summary>Enum and short options.</summary>
	/// <param name="color">-c,--colour, Pick a color.</param>
	/// <param name="name">-n,--name, Display name</param>
	public static void EnumCmd(TestColor color, string name) =>
		System.Console.Out.WriteLine($"ok:{color}:{name}");

	public static void Deploy([AsParameters("app")] DeployCliArgs args) =>
		System.Console.Out.WriteLine($"deploy:{args.Env}:{args.Port}");

	public static void Tags(List<string> tags) =>
		System.Console.Out.WriteLine("tags:" + string.Join(",", tags));

	// For bool? test
	public static void DryRunCmd(bool? dryRun = null) =>
		System.Console.Out.WriteLine($"dry-run:{dryRun?.ToString().ToLower() ?? "null"}");

	// For int parsing test
	public static void CountCmd(int count) =>
		System.Console.Out.WriteLine($"count:{count}");

	// For FileInfo test
	public static void FileCmd(FileInfo file) =>
		System.Console.Out.WriteLine($"file:{file.Name}");

	// For DirectoryInfo test
	public static void DirCmd(DirectoryInfo dir) =>
		System.Console.Out.WriteLine($"dir:{dir.Name}");

	// For Uri test
	public static void UriCmd(Uri uri) =>
		System.Console.Out.WriteLine($"uri:{uri.Host}");

	// For custom parser test
	public static void PointCmd([ArgumentParser(typeof(PointParser))] Point point) =>
		System.Console.Out.WriteLine($"point:{point.X},{point.Y}");

	internal readonly record struct Point(int X, int Y);

	internal sealed class PointParser : IArgumentParser<Point>
	{
		public bool TryParse(string raw, out Point value)
		{
			value = default;
			var parts = raw.Split(',');
			if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
			{ value = new Point(x, y); return true; }
			return false;
		}
	}
}
