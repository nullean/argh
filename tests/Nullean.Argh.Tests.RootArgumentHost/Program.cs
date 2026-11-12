using Nullean.Argh;
using Nullean.Argh.Tests.RootArgumentHost;

var app = new ArghApp();
app.MapAndRootAlias<ValidateCommands>();
app.Map("inspect", Handlers.Inspect);
app.MapNamespace("feed", "Feed commands; the namespace root takes an argument.", g =>
{
	g.MapRoot(Handlers.FeedRoot);
	g.Map("list", Handlers.FeedList);
});

return await app.RunAsync(args);
