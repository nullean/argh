namespace Nullean.Argh.Tests.Fixtures;

/// <summary>Global CLI flags (tests).</summary>
internal class TestGlobalCliOptions
{
	public bool Verbose { get; set; }
}

/// <summary>Storage command namespace options; must inherit global options type.</summary>
internal sealed class TestStorageCommandNamespaceOptions : TestGlobalCliOptions
{
	public string Prefix { get; set; } = "";
}

/// <summary>Commands under <c>storage</c>; nested class becomes <c>storage blob</c> nested namespace.</summary>
internal sealed class StorageCliCommands
{
	public static void List(TestStorageCommandNamespaceOptions o) => Console.Out.WriteLine("storage-list");

	public sealed class BlobCommands
	{
		public static void Upload(TestStorageCommandNamespaceOptions o) => Console.Out.WriteLine("blob-upload");
	}
}

internal interface IDiProbeService
{
	string Marker { get; }
}

internal sealed class DiProbeService : IDiProbeService
{
	public string Marker => "from-di";
}

/// <summary>Instance command type for DI resolution tests (<c>ArghServices.ServiceProvider</c>).</summary>
internal sealed class DiProbeCommands(IDiProbeService svc)
{
	public void Ping(TestGlobalCliOptions g) =>
		Console.Out.WriteLine($"probe:{svc.Marker}");
}
