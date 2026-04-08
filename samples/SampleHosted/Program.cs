using Microsoft.Extensions.Hosting;
using Nullean.Argh;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArgh(
	args,
	app => app.Add("hello", Handlers.Hello),
	() => ArghGenerated.RunAsync(args));

using var host = builder.Build();
await host.RunAsync();

/// <summary>Says hello to someone.</summary>
internal static class Handlers
{
	public static void Hello(string name) => Console.WriteLine($"Hello, {name}!");
}
