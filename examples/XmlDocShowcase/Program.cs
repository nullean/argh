// Example: rich XML documentation in CLI help (run with and without NO_COLOR).
//   dotnet run --project examples/XmlDocShowcase -- welcome --help

using Nullean.Argh;
using XmlDocShowcase;

var app = new ArghApp();
app.Add("welcome", DocsCommands.Welcome);
app.Add("ping", DocsCommands.Ping);

return await app.RunAsync(args);
