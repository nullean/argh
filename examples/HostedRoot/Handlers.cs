using Microsoft.Extensions.Logging;
using Nullean.Argh;

namespace HostedRoot;

/// <summary>Default handler when the app is invoked with no subcommand.</summary>
internal static class HostedRootDefaults
{
	/// <summary>App root: runs when you omit a namespace or subcommand.</summary>
	public static void App(HostedRootGlobalCliOptions g) =>
		Console.WriteLine("hosted-root:app-default");

}

/// <summary>Named command registered with <c>Add(string, …)</c> (shows as a top-level command in help).</summary>
internal static class HostedRootHello
{
	/// <summary>Greets by name.</summary>
	/// <param name="g">Injected global CLI options.</param>
	/// <param name="name">-n,--name, Name.</param>
	public static void Run(HostedRootGlobalCliOptions g, string name) =>
		Console.WriteLine($"hosted-root:hello:{name}");
}

/// <summary><c>storage</c> subcommands.</summary>
[NamespaceSegment("storage")]
internal sealed class HostedRootStorageCommands(ILogger<HostedRootStorageCommands> logger)
{
	/// <summary>Storage namespace root: runs when <c>storage</c> is selected but no deeper command.</summary>
	[DefaultCommand]
	public static void StorageNamespace(HostedRootStorageNamespaceOptions o) =>
		Console.WriteLine("hosted-root:storage-namespace-default");

	/// <summary>List storage items.</summary>
	/// <remarks>Does not modify remote state.</remarks>
	public void List(HostedRootStorageNamespaceOptions o)
	{
		logger.LogInformation("storage list");
		Console.WriteLine("hosted-root:storage:list");
	}

	internal sealed class BlobCommands(ILogger<HostedRootStorageCommands> logger)
	{
		/// <summary>Deletes a blob</summary>
		public void Delete(HostedRootStorageNamespaceOptions o)
		{
			logger.LogInformation("storage blob delete");
			Console.WriteLine("hosted-root:storage:blob:delete");
		}
	}
}
