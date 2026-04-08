// Hosted example: generic host + DI + AddArgh (no ArghGenerated lambda).
// Run: dotnet run --project examples/Hosted -- --help
//      dotnet run --project examples/Hosted -- hosted-cli hello --name Argh

using Hosted;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(c => c.AddConsole().SetMinimumLevel(LogLevel.Information));

builder.Services.AddSingleton<HostedGlobalFilter>();
builder.Services.AddSingleton<HostedPerCommandFilter>();

builder.Services.AddArgh(
	args,
	app =>
	{
		app.UseFilter<HostedGlobalFilter>();
		app.GlobalOptions<HostedGlobalCliOptions>();
		app.Add<HostedCliCommands>();
		app.AddNamespace("storage", g =>
		{
			g.CommandNamespaceOptions<HostedStorageCommandNamespaceOptions>();
			g.Add<HostedStorageCommands>();
		});
	});

using var host = builder.Build();
await host.RunAsync();
