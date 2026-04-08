// Basic console example for Nullean.Argh.
// Run: dotnet run --project examples/Basic -- --help
//      dotnet run --project examples/Basic -- hello --name Argh
//      dotnet run --project examples/Basic -- storage blob upload
//      dotnet run --project examples/Basic -- --version

using Basic;
using Nullean.Argh;

var app = new ArghApp();
app.UseFilter<GlobalExampleFilter>();
app.GlobalOptions<GlobalCliOptions>();
app.Add("hello", CommandHandlers.Hello);
app.Add("status", CommandHandlers.Status);
app.Add("deploy", CommandHandlers.Deploy);
app.Add("labels", CommandHandlers.Labels);
app.Group("storage", g =>
{
	g.GroupOptions<StorageGroupOptions>();
	g.Add<StorageCommands>();
});

return await app.RunAsync(args);
