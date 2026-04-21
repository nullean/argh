using Nullean.Argh;
using Nullean.Argh.Parsing;

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

/// <summary>Record with nullable value types for <see cref="AsParametersAttribute"/> binding coverage.</summary>
internal sealed record NullableNumericAsParamsArgs(int? Rps, int? MaxPages);

internal static class CliTestHandlers
{
	/// <summary>Greet someone by name.</summary>
	/// <remarks>See <see cref="CliRegistrationModule.DocLambdaEcho"/>; set <paramref name="name"/>.</remarks>
	/// <example>hello --name world</example>
	/// <param name="g">Injected global CLI options.</param>
	/// <param name="name">The name to greet.</param>
	[MiddlewareAttribute<TestsPerCommandMiddleware>]
	public static void Hello(TestGlobalCliOptions g, string name) =>
		Console.Out.WriteLine($"ok:{name}");

	/// <summary>Enum and short options.</summary>
	/// <param name="g">Injected global CLI options.</param>
	/// <param name="color">-c,--colour, Pick a color.</param>
	/// <param name="name">-n,--name, Display name</param>
	public static void EnumCmd(TestGlobalCliOptions g, TestColor color, string name) =>
		Console.Out.WriteLine($"ok:{color}:{name}");

	public static void Deploy(TestGlobalCliOptions g, [AsParameters("app")] DeployCliArgs args) =>
		Console.Out.WriteLine($"deploy:{args.Env}:{args.Port}");

	public static void NullableNumericAsParams(TestGlobalCliOptions g, [AsParameters("labs")] NullableNumericAsParamsArgs args) =>
		Console.Out.WriteLine($"nullable-numeric:{args.Rps?.ToString() ?? "null"}:{args.MaxPages?.ToString() ?? "null"}");

	public static void Tags(TestGlobalCliOptions g, List<string> tags) =>
		Console.Out.WriteLine("tags:" + string.Join(",", tags));

	// For bool? test
	public static void DryRunCmd(TestGlobalCliOptions g, bool? dryRun = null) =>
		Console.Out.WriteLine($"dry-run:{dryRun?.ToString().ToLower() ?? "null"}");

	// For int parsing test
	public static void CountCmd(TestGlobalCliOptions g, int count) =>
		Console.Out.WriteLine($"count:{count}");

	// For FileInfo test
	public static void FileCmd(TestGlobalCliOptions g, FileInfo file) =>
		Console.Out.WriteLine($"file:{file.Name}");

	// For DirectoryInfo test
	public static void DirCmd(TestGlobalCliOptions g, DirectoryInfo dir) =>
		Console.Out.WriteLine($"dir:{dir.Name}");

	// For Uri test
	public static void UriCmd(TestGlobalCliOptions g, Uri uri) =>
		Console.Out.WriteLine($"uri:{uri.Host}");

	/// <param name="g">Injected global CLI options.</param>
	/// <param name="duration">Duration flag.</param>
	/// <param name="on">A calendar date.</param>
	public static void TemporalCmd(TestGlobalCliOptions g, TimeSpan duration, DateOnly on) =>
		Console.Out.WriteLine($"temporal:{duration.TotalSeconds}:{on:yyyy-MM-dd}");

	// For custom parser test
	public static void PointCmd(TestGlobalCliOptions g, [ArgumentParser(typeof(PointParser))] Point point) =>
		Console.Out.WriteLine($"point:{point.X},{point.Y}");

	internal readonly record struct Point(int X, int Y);

	internal sealed class PointParser : IArgumentParser<Point>
	{
		public bool TryParse(string raw, out Point value)
		{
			value = default;
			var parts = raw.Split(',');
			if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
			{ value = new Point(x, y); return true; }
			return false;
		}
	}
}
