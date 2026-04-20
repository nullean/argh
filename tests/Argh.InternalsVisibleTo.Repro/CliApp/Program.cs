// Minimal CLI — referenced by test project with InternalsVisibleTo to guard CS0436 (duplicate generated types).
using Nullean.Argh;

var app = new ArghApp();
app.Map("demo", () => Task.FromResult(0));
return await app.RunAsync(args);
