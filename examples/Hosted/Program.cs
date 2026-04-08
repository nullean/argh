// Hosted example: generic host + DI + AddArgh (no ArghGenerated lambda).
// Run: dotnet run --project examples/Hosted -- --help
//      dotnet run --project examples/Hosted -- hello --name Argh

using Hosted;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nullean.Argh;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(c => c.AddConsole().SetMinimumLevel(LogLevel.Information));

builder.Services.AddSingleton<HostedGlobalFilter>();
builder.Services.AddSingleton<HostedPerCommandFilter>();
builder.Services.AddTransient<HostedCliCommands>();
builder.Services.AddTransient<HostedStorageCommands>();
builder.Services.AddTransient<HostedStorageCommands.BlobCommands>();

builder.Services.AddArgh(
	args,
	app =>
	{
		app.UseFilter<HostedGlobalFilter>();
		app.GlobalOptions<HostedGlobalCliOptions>();
		app.Add<HostedCliCommands>();
		app.Group("storage", g =>
		{
			g.GroupOptions<HostedStorageGroupOptions>();
			g.Add<HostedStorageCommands>();
		});
	});

using var host = builder.Build();
await host.RunAsync();
