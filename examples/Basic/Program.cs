// Basic console example for Nullean.Argh.
// Run:
//   dotnet run --project examples/Basic -- --help
//   dotnet run --project examples/Basic -- hello --name Argh
//   dotnet run --project examples/Basic -- storage blob upload
//   dotnet run --project examples/Basic -- api version
//   dotnet run --project examples/Basic -- doc-echo --line "hi"
//   dotnet run --project examples/Basic -- quick-echo --msg ping
//   dotnet run --project examples/Basic -- --version
//   dotnet run --project examples/Basic -- --completions bash

using Basic;
using Nullean.Argh;

var app = new ArghApp();
// Filters implement the extensible pipeline (middleware): global filters run in registration order, then per-command filters from attributes.
app.UseFilter<GlobalExampleFilter>();
app.UseFilter<OrderingDemoFilter>();
app.GlobalOptions<GlobalCliOptions>();
app.Add("hello", CommandHandlers.Hello);
app.Add("status", CommandHandlers.Status);
app.Add("deploy", CommandHandlers.Deploy);
app.Add("labels", CommandHandlers.Labels);
// Named handler with XML (see DocEcho below) — help text is generated from documentation.
app.Add("doc-echo", LocalCliHandlers.DocEcho);
// Anonymous lambda: no XML on the delegate; use DocEcho-style handlers when you want rich --help.
app.Add("quick-echo", (string msg) => Console.WriteLine($"basic:quick:{msg}"));
app.AddNamespace("storage", g =>
{
	g.CommandNamespaceOptions<StorageCommandNamespaceOptions>();
	g.Add<StorageCommands>();
});
app.AddNamespace("api", g =>
{
	g.CommandNamespaceOptions<ApiNamespaceOptions>();
	g.Add<ApiCommands>();
});

return await app.RunAsync(args);
