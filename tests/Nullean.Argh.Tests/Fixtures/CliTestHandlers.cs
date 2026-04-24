using System.Linq;
using System.Threading;
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

/// <summary><see cref="AsParametersAttribute"/> record with injected <see cref="CancellationToken"/> (must follow all CLI-bound members).</summary>
internal sealed record AsParamsWithCtArgs(string Env, int Port, CancellationToken Ct);

/// <summary>Record with nullable value types for <see cref="AsParametersAttribute"/> binding coverage.</summary>
internal sealed record NullableNumericAsParamsArgs(int? Rps, int? MaxPages);

/// <summary>Record with optional <see cref="Uri"/> for <see cref="AsParametersAttribute"/> binding coverage.</summary>
internal sealed record OptionalUriAsParamsArgs(Uri? Endpoint);

/// <summary>Record with <see cref="IReadOnlySet{T}"/> for set-binding coverage.</summary>
/// <param name="TagIds">Set of integer tag IDs.</param>
internal sealed record TagSetArgs(IReadOnlySet<int> TagIds);

/// <summary>Init-only bound object for XML documentation in help output.</summary>
internal sealed class PropDocBoundArgs
{
	/// <summary>Argh_help_doc_alpha_unique.</summary>
	public string Alpha { get; init; } = "";

	/// <summary>Argh_help_doc_beta_unique.</summary>
	public int Beta { get; init; }
}

internal sealed record ParamCommentRecord(
	/// <summary>Argh_help_doc_gamma_unique.</summary>
	string Gamma);

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

	public static void AsParamsWithCt(TestGlobalCliOptions g, [AsParameters("run")] AsParamsWithCtArgs args) =>
		Console.Out.WriteLine($"as-params-ct:{args.Env}:{args.Port}:{args.Ct.CanBeCanceled}");

	public static void NullableNumericAsParams(TestGlobalCliOptions g, [AsParameters("labs")] NullableNumericAsParamsArgs args) =>
		Console.Out.WriteLine($"nullable-numeric:{args.Rps?.ToString() ?? "null"}:{args.MaxPages?.ToString() ?? "null"}");

	public static void OptionalUriAsParams(TestGlobalCliOptions g, [AsParameters] OptionalUriAsParamsArgs args) =>
		Console.Out.WriteLine($"optional-uri:{args.Endpoint?.ToString() ?? "null"}");

	public static void PropDocAsParams(TestGlobalCliOptions g, [AsParameters] PropDocBoundArgs args) =>
		Console.Out.WriteLine($"prop-doc:{args.Alpha}:{args.Beta}");

	public static void ParamCommentRecordCmd(TestGlobalCliOptions g, [AsParameters] ParamCommentRecord r) =>
		Console.Out.WriteLine($"param-comment:{r.Gamma}");

	public static void Tags(TestGlobalCliOptions g, List<string> tags) =>
		Console.Out.WriteLine("tags:" + string.Join(",", tags));

	/// <summary>Regression: braces in XML docs must not become C# interpolation in generated help.</summary>
	/// <param name="g">Injected global CLI options.</param>
	/// <param name="description">Supports {version} and {owner} placeholders.</param>
	public static void BraceDoc(TestGlobalCliOptions g, string? description = null) =>
		Console.Out.WriteLine("brace-doc:" + (description ?? ""));

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

	// IReadOnlySet<T> handlers
	public static void TagSet(TestGlobalCliOptions g, IReadOnlySet<int> tagIds) =>
		Console.Out.WriteLine("tag-set:" + string.Join(",", tagIds.OrderBy(x => x)));

	public static void ColorSet(TestGlobalCliOptions g, IReadOnlySet<TestColor> colors) =>
		Console.Out.WriteLine("color-set:" + string.Join(",", colors.OrderBy(x => (int)x)));

	public static void OptTagSet(TestGlobalCliOptions g, IReadOnlySet<int>? tagIds) =>
		Console.Out.WriteLine("opt-tag-set:" + (tagIds is null or { Count: 0 } ? "none" : string.Join(",", tagIds.OrderBy(x => x))));

	public static void AsParamsTagSet(TestGlobalCliOptions g, [AsParameters] TagSetArgs args) =>
		Console.Out.WriteLine("as-params-tag-set:" + string.Join(",", args.TagIds.OrderBy(x => x)));

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

/// <summary>Handlers under <c>billing tools</c> for AGH0022 nested-namespace regression.</summary>
internal static class BillingToolsHandlers
{
	public static void Status(TestGlobalCliOptions g) =>
		Console.Out.WriteLine("billing-tools-status");
}

/// <summary>Handlers under <c>support tools</c> for AGH0022 nested-namespace regression.</summary>
internal static class SupportToolsHandlers
{
	public static void Status(TestGlobalCliOptions g) =>
		Console.Out.WriteLine("support-tools-status");
}
