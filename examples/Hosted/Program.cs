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
using Nullean.Argh;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var verbose = HostedGlobalCliOptions.TryParseArgh(args, out var options) && options.Verbose;

builder.Services.AddLogging(c => c
	.AddConsole()
	.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information)
);

builder.Services.AddSingleton<HostedGlobalMiddleware>();
builder.Services.AddSingleton<HostedOrderingDemoMiddleware>();
builder.Services.AddSingleton<HostedPerCommandMiddleware>();

builder.Services.AddArgh(
	args,
	app =>
	{
		app.UseMiddleware<HostedGlobalMiddleware>();
		app.UseMiddleware<HostedOrderingDemoMiddleware>();
		app.UseGlobalOptions<HostedGlobalCliOptions>();
		app.Map<HostedCliCommands>();
		app.Map("doc-echo", HostedLocalHandlers.DocEcho);
		app.Map("quick-echo", (string msg) => Console.WriteLine($"hosted:quick:{msg}"));
		app.MapNamespace<HostedStorageCommands>("storage", g =>
		{
			g.UseNamespaceOptions<HostedStorageCommandNamespaceOptions>();
		});
		app.MapNamespace<HostedApiCommands>("api", g =>
		{
			g.UseNamespaceOptions<HostedApiNamespaceOptions>();
		});
	});

using var host = builder.Build();
await host.RunAsync();
