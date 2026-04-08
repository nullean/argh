using System.Runtime.CompilerServices;
using Nullean.Argh;
using Nullean.Argh.Tests.Fixtures;

namespace Nullean.Argh.Tests;

internal static class CliRegistrationModule
{
	[ModuleInitializer]
	internal static void RegisterCommands()
	{
		var app = new ArghApp();
		app.UseFilter<TestsGlobalFilter>();
		app.GlobalOptions<TestGlobalCliOptions>();
		app.Add("hello", CliTestHandlers.Hello);
		app.Add("enum-cmd", CliTestHandlers.EnumCmd);
		app.Add("deploy", CliTestHandlers.Deploy);
		app.Add("tags", CliTestHandlers.Tags);
		app.Group("storage", g =>
		{
			g.GroupOptions<TestStorageCliOptions>();
			g.Add<StorageCliCommands>();
		});
	}
}
