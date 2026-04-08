using Nullean.Argh;

namespace Basic;

internal enum ExampleColor
{
	/// <summary>Red channel.</summary>
	Red,
	/// <summary>Blue channel.</summary>
	Blue,
}

/// <summary>Deployment target bound via <see cref="AsParametersAttribute"/>.</summary>
/// <param name="Env">-e,--env, Environment name (e.g. staging).</param>
/// <param name="Port">-p,--port, Listen port.</param>
internal sealed record DeployAppArgs(string Env, int Port);

/// <summary>Static handlers for flat commands.</summary>
internal static class CommandHandlers
{
	/// <summary>Greets someone by name.</summary>
	/// <param name="name">-n,--name, Name to greet.</param>
	[FilterAttribute<PerCommandExampleFilter>]
	public static void Hello(string name) =>
		Console.WriteLine($"Hello, {name}!");

	/// <summary>Prints a colored status line.</summary>
	/// <param name="color">-c,--color, Accent color.</param>
	/// <param name="label">-l,--label, Short label.</param>
	public static void Status(ExampleColor color, string label) =>
		Console.WriteLine($"status:{color}:{label}");

	/// <summary>Deploy using a prefixed <c>app</c> parameter object.</summary>
	public static void Deploy([AsParameters("app")] DeployAppArgs app) =>
		Console.WriteLine($"deploy env={app.Env} port={app.Port}");

	/// <summary>Repeated <c>--label</c> flags build a list.</summary>
	/// <param name="labels">--label, Labels to print.</param>
	public static void Labels(List<string> labels) =>
		Console.WriteLine("labels: " + string.Join(", ", labels));
}

/// <summary>Command group <c>storage</c> with a nested <c>blob</c> subgroup.</summary>
internal sealed class StorageCommands
{
	/// <summary>Lists objects under the configured prefix.</summary>
	public void List() => Console.WriteLine("storage:list (use --verbose and storage --prefix with global/group options)");

	/// <summary>Nested subgroup mapped to <c>storage blob …</c>.</summary>
	public sealed class BlobCommands
	{
		/// <summary>Upload placeholder.</summary>
		public void Upload() =>
			Console.WriteLine("storage:blob:upload");
	}
}
