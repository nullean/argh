// Hosted example: generic host + DI + AddArgh.
// Run:
//   dotnet run --project examples/Hosted -- --help
//   dotnet run --project examples/Hosted -- hello --name Argh
//   dotnet run --project examples/Hosted -- doc-echo --token x
//   dotnet run --project examples/Hosted -- quick-echo --msg y
//   dotnet run --project examples/Hosted -- api version

using Hosted;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(c => c.AddConsole().SetMinimumLevel(LogLevel.Information));

builder.Services.AddSingleton<HostedGlobalMiddleware>();
builder.Services.AddSingleton<HostedOrderingDemoMiddleware>();
builder.Services.AddSingleton<HostedPerCommandMiddleware>();

builder.Services.AddArgh(
	args,
	app =>
	{
		app.UseMiddleware<HostedGlobalMiddleware>();
		app.UseMiddleware<HostedOrderingDemoMiddleware>();
		app.GlobalOptions<HostedGlobalCliOptions>();
		app.Add<HostedCliCommands>();
		app.Add("doc-echo", HostedLocalHandlers.DocEcho);
		app.Add("quick-echo", (string msg) => Console.WriteLine($"hosted:quick:{msg}"));
		app.AddNamespace("storage", g =>
		{
			g.CommandNamespaceOptions<HostedStorageCommandNamespaceOptions>();
			g.Add<HostedStorageCommands>();
		});
		app.AddNamespace("api", g =>
		{
			g.CommandNamespaceOptions<HostedApiNamespaceOptions>();
			g.Add<HostedApiCommands>();
		});
	});

using var host = builder.Build();
await host.RunAsync();
