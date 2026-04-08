namespace Nullean.Argh.Tests.Fixtures;

/// <summary>Global CLI flags (tests).</summary>
internal class TestGlobalCliOptions
{
	public bool Verbose { get; set; }
}

/// <summary>Storage group options; must inherit global options type.</summary>
internal sealed class TestStorageCliOptions : TestGlobalCliOptions
{
	public string Prefix { get; set; } = "";
}

/// <summary>Commands under <c>storage</c>; nested class becomes <c>storage blob</c> subgroup.</summary>
internal sealed class StorageCliCommands
{
	public static void List()
	{
		System.Console.Out.WriteLine("storage-list");
	}

	public sealed class BlobCommands
	{
		public static void Upload()
		{
			System.Console.Out.WriteLine("blob-upload");
		}
	}
}
