using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nullean.Argh;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging();
builder.Services.AddTransient<HostedSampleCommands>();

builder.Services.AddArgh(
	args,
	app => app.Add<HostedSampleCommands>()
);

using var host = builder.Build();
await host.RunAsync();

/// <summary>Sample commands with constructor injection when running under the generic host.</summary>
internal sealed class HostedSampleCommands
{
	private readonly ILogger<HostedSampleCommands> _logger;

	public HostedSampleCommands(ILogger<HostedSampleCommands> logger) =>
		_logger = logger;

	/// <summary>Greets by name.</summary>
	/// <param name="name">-n,--name, Who to greet</param>
	public void Greet(string name)
	{
		_logger.LogInformation("Greet invoked");
		Console.WriteLine($"Hello, {name}!");
	}
}
