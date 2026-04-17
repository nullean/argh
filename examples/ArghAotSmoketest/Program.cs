// Native AOT smoketest — CI publishes and runs this binary to guard trim/AOT compatibility.
using Nullean.Argh;

var app = new ArghApp();
app.Add("ping", () => { Console.WriteLine("pong"); return Task.FromResult(0); });

return await app.RunAsync(args);
