using System.Runtime.CompilerServices;
using Nullean.Argh;
using Nullean.Argh.Tests.Fixtures;

namespace Nullean.Argh.Tests;

internal static class CliRegistrationModule
{
	[ModuleInitializer]
	internal static void RegisterCommands()
	{
		IArghBuilder app = new ArghBuilder();
		app.UseFilter<TestsGlobalFilter>();
		app.GlobalOptions<TestGlobalCliOptions>();
		app.Add("hello", CliTestHandlers.Hello);
		app.Add("enum-cmd", CliTestHandlers.EnumCmd);
		app.Add("deploy", CliTestHandlers.Deploy);
		app.Add("tags", CliTestHandlers.Tags);
		app.Add("dry-run-cmd", CliTestHandlers.DryRunCmd);
		app.Add("count-cmd", CliTestHandlers.CountCmd);
		app.Add("file-cmd", CliTestHandlers.FileCmd);
		app.Add("dir-cmd", CliTestHandlers.DirCmd);
		app.Add("uri-cmd", CliTestHandlers.UriCmd);
		app.Add("point-cmd", CliTestHandlers.PointCmd);
		app.Add("lambda-cmd", (string msg) => Console.Out.WriteLine($"lambda:{msg}"));
		app.Add<DiProbeCommands>();
		app.Group("storage", g =>
		{
			g.GroupOptions<TestStorageCliOptions>();
			g.Add<StorageCliCommands>();
		});
	}
}
