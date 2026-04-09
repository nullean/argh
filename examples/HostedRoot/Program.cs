// Hosted example: generic host + DI + AddArgh, with explicit root and namespace-root handlers.
//
// Try:
//   dotnet run --project examples/HostedRoot -- --help
//   dotnet run --project examples/HostedRoot --                    # app root (no subcommand)
//   dotnet run --project examples/HostedRoot -- --verbose          # globals only → app root
//   dotnet run --project examples/HostedRoot -- hello --name Argh
//   dotnet run --project examples/HostedRoot -- storage            # storage namespace root
//   dotnet run --project examples/HostedRoot -- storage list

using HostedRoot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(c => c.AddConsole().SetMinimumLevel(LogLevel.Information));
builder.Services.AddSingleton<HostedRootGlobalMiddleware>();

builder.Services.AddArgh(
	args,
	app =>
	{
		app.UseMiddleware<HostedRootGlobalMiddleware>();
		app.GlobalOptions<HostedRootGlobalCliOptions>();
		app.AddRootCommand(HostedRootDefaults.App);
		app.Add("hello", HostedRootHello.Run);
		app.AddNamespace<HostedRootStorageCommands>("storage", g =>
		{
			g.CommandNamespaceOptions<HostedRootStorageNamespaceOptions>();
			g.AddNamespaceRootCommand(HostedRootDefaults.StorageNamespace);
		});
	});

using var host = builder.Build();
await host.RunAsync();
