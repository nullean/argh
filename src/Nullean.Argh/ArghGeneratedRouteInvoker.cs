using System.Reflection;

namespace Nullean.Argh;

internal static class ArghGeneratedRouteInvoker
{
	private static readonly Lazy<Func<string, RouteMatch?>> RouteLazy = new(ResolveRoute);

	private static Func<string, RouteMatch?> ResolveRoute()
	{
		const string typeName = "Nullean.Argh.ArghGenerated";
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type? t = assembly.GetType(typeName, throwOnError: false);
			if (t is null)
				continue;
			MethodInfo? m = t.GetMethod(
				"Route",
				BindingFlags.Public | BindingFlags.Static,
				null,
				new[] { typeof(string) },
				null);
			if (m is null)
				continue;
			return (Func<string, RouteMatch?>)Delegate.CreateDelegate(typeof(Func<string, RouteMatch?>), m);
		}

		throw new InvalidOperationException(
			"Could not find generated Nullean.Argh.ArghGenerated.Route(string). Reference Nullean.Argh, register commands with ArghApp, and ensure the source generator runs so ArghGenerated is emitted.");
	}

	internal static RouteMatch? Route(string commandLine) => RouteLazy.Value(commandLine);
}
