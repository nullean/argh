namespace Nullean.Argh.Tests.Fixtures;

/// <summary>Enum used by <see cref="TestGlobalCliOptions"/> for global-option default parsing tests.</summary>
internal enum FixtureSeverity
{
	Trace,
	Information,
	Warning
}

/// <summary>Global CLI flags (tests).</summary>
internal class TestGlobalCliOptions
{
	public bool Verbose { get; set; }

	/// <summary>Enum default for global-flag parsing regression.</summary>
	public FixtureSeverity Severity { get; set; } = FixtureSeverity.Information;
}

/// <summary>Storage command namespace options; must inherit global options type.</summary>
internal sealed class TestStorageCommandNamespaceOptions : TestGlobalCliOptions
{
	public string Prefix { get; set; } = "";
}

/// <summary>Commands under <c>storage</c>. Nested <see cref="BlobCommands"/> must be registered explicitly via <c>MapNamespace&lt;BlobCommands&gt;</c>.</summary>
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

/// <summary>Commands whose CLI name is overridden via <c>[CommandName]</c>.</summary>
internal sealed class CommandNameOverrideCommands
{
	[CommandName("renamed-cmd")]
	public static void OriginalMethodName(TestGlobalCliOptions g) =>
		Console.Out.WriteLine("marker:renamed-cmd");
}
