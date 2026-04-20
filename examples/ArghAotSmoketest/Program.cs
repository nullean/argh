// Native AOT smoketest — CI publishes and runs this binary to guard trim/AOT compatibility.
// Exercises AddArgh + Map&lt;T&gt; / MapNamespace&lt;T&gt; (DI registration + trim annotations).
using ArghAotSmoketest;
using Microsoft.Extensions.Hosting;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddArgh(args, app =>
{
	app.Map<SmokeCmds>();
	app.MapNamespace<NsCmds>("ns", _ => { });
});

await builder.Build().RunAsync();
