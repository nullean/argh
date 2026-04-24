using System.Threading;

namespace Nullean.Argh.Tests.Fixtures;

/// <summary>Command class for <c>MapAndRootAlias</c> integration tests — multi-command type with a designated alias target.</summary>
internal sealed class RootAliasTestCommands
{
	/// <summary>Build the documentation set. This is the alias target for the root-alias namespace.</summary>
	/// <remarks>Runs incrementally by default; use <c>--force</c> to rebuild everything.</remarks>
	/// <example>alias-scope --output .artifacts/html</example>
	/// <example>alias-scope build --output .artifacts/html</example>
	/// <param name="g">Injected global CLI options.</param>
	/// <param name="output">-o,--output, Output folder.</param>
	/// <param name="force">--force, Force a full rebuild.</param>
	[DefaultCommand]
	public static void Build(TestGlobalCliOptions g, string? output, bool? force, CancellationToken ct) =>
		Console.Out.WriteLine($"marker:alias-build output={output ?? "null"} force={force?.ToString() ?? "null"}");

	/// <summary>Serve the documentation set locally.</summary>
	/// <param name="g">Injected global CLI options.</param>
	/// <param name="port">-p,--port, Port to listen on.</param>
	public static void Serve(TestGlobalCliOptions g, int port = 3000, CancellationToken ct = default) =>
		Console.Out.WriteLine($"marker:alias-serve port={port}");
}
