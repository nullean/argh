using System.Collections.Generic;
using Nullean.Argh;

namespace Nullean.Argh.Tests.Fixtures;

internal enum TestColor
{
	Red,
	Blue
}

/// <summary>Sample record bound via <see cref="AsParametersAttribute"/>.</summary>
/// <param name="Env">Deployment environment name.</param>
/// <param name="Port">Listen port.</param>
internal sealed record DeployCliArgs(string Env, int Port);

internal static class CliTestHandlers
{
	[FilterAttribute<TestsPerCommandFilter>]
	public static void Hello(string name) =>
		System.Console.Out.WriteLine($"ok:{name}");

	/// <summary>Enum and short options.</summary>
	/// <param name="color">-c,--colour, Pick a color</param>
	/// <param name="name">-n,--name, Display name</param>
	public static void EnumCmd(TestColor color, string name) =>
		System.Console.Out.WriteLine($"ok:{color}:{name}");

	public static void Deploy([AsParameters("app")] DeployCliArgs args) =>
		System.Console.Out.WriteLine($"deploy:{args.Env}:{args.Port}");

	public static void Tags(List<string> tags) =>
		System.Console.Out.WriteLine("tags:" + string.Join(",", tags));
}
