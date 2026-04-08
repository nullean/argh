namespace Basic;

/// <summary>Options for the <c>storage</c> group; must inherit the global options type.</summary>
public sealed class StorageGroupOptions : GlobalCliOptions
{
	/// <summary>Prefix filter for listings.</summary>
	public string Prefix { get; set; } = "";
}
