namespace Nullean.Argh.Tests.RootArgumentHost;

internal sealed class ValidateCommands
{
	/// <summary>Validate a package. The app-root alias target, and its first parameter is an argument.</summary>
	/// <param name="path">-, --path, Path to the package to validate.</param>
	/// <param name="expectedVersion">-v, --expected-version, Version the package must carry.</param>
	/// <param name="skipReleaseMode">-r, --skip-release-mode, Skip the release-mode check.</param>
	[DefaultCommand]
	public static void Validate([Argument] string path, string? expectedVersion = null, bool skipReleaseMode = false) =>
		Console.Out.WriteLine($"marker:validate path={path} version={expectedVersion ?? "null"} skip={skipReleaseMode}");
}

internal static class Handlers
{
	/// <summary>Inspect something; a named command next to the root alias.</summary>
	public static void Inspect() => Console.Out.WriteLine("marker:inspect");

	/// <summary>The feed namespace default.</summary>
	/// <param name="name">Name of the feed.</param>
	public static void FeedRoot([Argument] string name) => Console.Out.WriteLine($"marker:feed-root name={name}");

	/// <summary>List the feeds.</summary>
	public static void FeedList() => Console.Out.WriteLine("marker:feed-list");
}
