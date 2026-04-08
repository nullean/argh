using Microsoft.Extensions.Logging;
using Nullean.Argh;

namespace Hosted;

internal enum HostedExampleColor
{
	Red,
	Blue,
}

/// <param name="Env">-e,--env, Environment.</param>
/// <param name="Port">-p,--port, Port.</param>
internal sealed record HostedDeployArgs(string Env, int Port);

/// <summary>Instance handlers resolved from DI (<c>Add&lt;T&gt;()</c>).</summary>
internal sealed class HostedCliCommands
{
	private readonly ILogger<HostedCliCommands> _logger;

	public HostedCliCommands(ILogger<HostedCliCommands> logger) =>
		_logger = logger;

	/// <summary>Greets by name.</summary>
	/// <param name="name">-n,--name, Name.</param>
	[FilterAttribute<HostedPerCommandFilter>]
	public void Hello(string name)
	{
		_logger.LogInformation("Hello invoked");
		Console.WriteLine($"Hello, {name}!");
	}

	/// <summary>Status with enum.</summary>
	/// <param name="color">-c,--color, Color.</param>
	/// <param name="label">-l,--label, Label.</param>
	public void Status(HostedExampleColor color, string label) =>
		Console.WriteLine($"status:{color}:{label}");

	/// <summary>Deploy with <see cref="AsParametersAttribute"/>.</summary>
	public void Deploy([AsParameters("app")] HostedDeployArgs app) =>
		Console.WriteLine($"deploy env={app.Env} port={app.Port}");

	/// <summary>Repeated labels.</summary>
	/// <param name="labels">--label, Values.</param>
	public void Labels(List<string> labels) =>
		Console.WriteLine("labels: " + string.Join(", ", labels));
}

/// <summary><c>storage</c> group with nested <c>blob</c> commands.</summary>
internal sealed class HostedStorageCommands(ILogger<HostedStorageCommands> logger)
{
	public void List()
	{
		logger.LogInformation("storage list");
		Console.WriteLine("storage:list");
	}

	public sealed class BlobCommands(ILogger<BlobCommands> log)
	{
		/// <summary>
		/// Binds flags from the command argv tail into <see cref="UploadOptions"/>
		/// (inherits group/global flags plus command-specific <see cref="UploadOptions.Target"/>).
		/// </summary>
		public void Upload([AsParameters] UploadOptions options)
		{
			log.LogInformation("blob upload");
			Console.WriteLine($"storage:blob:upload verbose={options.Verbose} prefix={options.Prefix} target={options.Target}");
		}
	}
}
