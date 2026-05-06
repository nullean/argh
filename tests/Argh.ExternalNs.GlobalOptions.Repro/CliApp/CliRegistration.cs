using System.Runtime.CompilerServices;
using External.Ns.Options;
using Nullean.Argh;
using Nullean.Argh.Builder;

// RootNamespace = Documentation.Builder (set in CliApp.csproj).
// ExternalNsGlobalOptions lives in External.Ns.Options (a completely different namespace).
// The generator must emit global::External.Ns.Options.ExternalNsGlobalOptions — not
// global::Documentation.Builder.ExternalNsGlobalOptions — in the generated code.

namespace Documentation.Builder;

internal static class CliRegistration
{
	[ModuleInitializer]
	internal static void Register()
	{
		IArghBuilder app = new ArghBuilder();
		app.UseGlobalOptions<ExternalNsGlobalOptions>();
		app.Map("echo", Commands.Echo);
	}
}

internal static class Commands
{
	/// <summary>Echo verbose flag and tag from global options.</summary>
	/// <param name="g">Injected global options.</param>
	public static void Echo(ExternalNsGlobalOptions g) =>
		System.Console.Out.WriteLine($"verbose={g.Verbose} tag={g.Tag}");
}
