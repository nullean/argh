// Basic console example for Nullean.Argh.
// Run:
//   dotnet run --project examples/Basic -- --help
//   dotnet run --project examples/Basic -- hello --name Argh
//   dotnet run --project examples/Basic -- storage blob upload
//   dotnet run --project examples/Basic -- api version
//   dotnet run --project examples/Basic -- doc-echo --line "hi"
//   dotnet run --project examples/Basic -- quick-echo --msg ping
//   dotnet run --project examples/Basic -- --version
//   dotnet run --project examples/Basic -- __completion bash

using Basic;
using Nullean.Argh;

var app = new ArghApp();
// Middleware: global registrations run in order, then per-command middleware from attributes.
app.UseMiddleware<GlobalExampleMiddleware>();
app.UseMiddleware<OrderingDemoMiddleware>();
app.UseGlobalOptions<GlobalCliOptions>();
app.Map("hello", CommandHandlers.Hello);
app.Map("status", CommandHandlers.Status);
app.Map("deploy", CommandHandlers.Deploy);
app.Map("labels", CommandHandlers.Labels);
// Named handler with XML (see DocEcho below) — help text is generated from documentation.
app.Map("doc-echo", LocalCliHandlers.DocEcho);
// Anonymous lambda: no XML on the delegate; use DocEcho-style handlers when you want rich --help.
app.Map("quick-echo", (string msg) => Console.WriteLine($"basic:quick:{msg}"));
app.MapNamespace<StorageCommands>("storage", g =>
{
	g.UseNamespaceOptions<StorageCommandNamespaceOptions>();
});
app.MapNamespace<ApiCommands>("api", g =>
{
	g.UseNamespaceOptions<ApiNamespaceOptions>();
});

return await app.RunAsync(args);
