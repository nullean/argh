// Example: rich XML documentation in CLI help (run with and without NO_COLOR).
//   dotnet run --project examples/XmlDocShowcase -- welcome --help
//   dotnet run --project examples/XmlDocShowcase -- docs inline --help

using Nullean.Argh;
using XmlDocShowcase;

var app = new ArghApp();
app.UseGlobalOptions<XmlDocShowcaseGlobalOptions>();
app.Map("welcome", DocsCommands.Welcome);
app.Map("ping", DocsCommands.Ping);
app.MapNamespace<XmlDocSamples>("docs", g => { g.UseNamespaceOptions<XmlDocNamespaceOptions>(); });

return await app.RunAsync(args);
