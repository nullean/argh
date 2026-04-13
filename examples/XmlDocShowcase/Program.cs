// Example: rich XML documentation in CLI help (run with and without NO_COLOR).
//   dotnet run --project examples/XmlDocShowcase -- welcome --help
//   dotnet run --project examples/XmlDocShowcase -- docs inline --help

using Nullean.Argh;
using XmlDocShowcase;

var app = new ArghApp();
app.GlobalOptions<XmlDocShowcaseGlobalOptions>();
app.Add("welcome", DocsCommands.Welcome);
app.Add("ping", DocsCommands.Ping);
app.AddNamespace<XmlDocSamples>("docs", g => { g.CommandNamespaceOptions<XmlDocNamespaceOptions>(); });

return await app.RunAsync(args);
