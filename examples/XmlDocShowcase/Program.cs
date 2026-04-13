// Example: rich XML documentation in CLI help (run with and without NO_COLOR).
//   dotnet run --project examples/XmlDocShowcase -- demo --help

using Nullean.Argh;
using XmlDocShowcase;

var app = new ArghApp();
app.Add("demo", DocsCommands.Demo);

return await app.RunAsync(args);
