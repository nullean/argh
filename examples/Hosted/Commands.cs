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
	[MiddlewareAttribute<HostedPerCommandMiddleware>]
	public void Hello(HostedGlobalCliOptions g, string name)
	{
		_logger.LogInformation("Hello invoked");
		Console.WriteLine($"hosted:hello:{name}");
	}

	/// <summary>Status with enum.</summary>
	/// <param name="color">-c,--color, Color.</param>
	/// <param name="label">-l,--label, Label.</param>
	public void Status(HostedGlobalCliOptions g, HostedExampleColor color, string label) =>
		Console.WriteLine($"status:{color}:{label}");

	/// <summary>Deploy with <see cref="AsParametersAttribute"/>.</summary>
	public void Deploy(HostedGlobalCliOptions g, [AsParameters("app")] HostedDeployArgs app) =>
		Console.WriteLine($"deploy env={app.Env} port={app.Port}");

	/// <summary>Repeated labels.</summary>
	/// <param name="labels">--label, Values.</param>
	public void Labels(HostedGlobalCliOptions g, List<string> labels) =>
		Console.WriteLine("labels: " + string.Join(", ", labels));
}

/// <summary><c>storage</c> group with nested <c>blob</c> commands.</summary>
internal sealed class HostedStorageCommands(ILogger<HostedStorageCommands> logger)
{
	public void List(HostedStorageCommandNamespaceOptions o)
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

/// <summary><c>api</c> grouped commands (DI).</summary>
internal sealed class HostedApiCommands(ILogger<HostedApiCommands> log)
{
	/// <summary>Print API version.</summary>
	public void Version(HostedApiNamespaceOptions o)
	{
		log.LogInformation("api version");
		Console.WriteLine("hosted:api:version:1");
	}
}
