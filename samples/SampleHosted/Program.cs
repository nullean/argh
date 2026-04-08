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
	(IArghBuilder app) => app.Add<HostedSampleCommands>(),
	() => ArghGenerated.RunAsync(args)
);

using var host = builder.Build();
await host.RunAsync();

/// <summary>Sample commands with constructor injection when running under the generic host.</summary>
internal sealed class HostedSampleCommands(ILogger<HostedSampleCommands> logger)
{
	/// <summary>Greets by name.</summary>
	/// <param name="name">-n,--name, Who to greet</param>
	public void Greet(string name)
	{
		logger.LogInformation("Greet invoked");
		Console.WriteLine($"Hello, {name}!");
	}
}
