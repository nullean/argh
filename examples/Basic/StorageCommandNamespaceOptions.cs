namespace Basic;

/// <summary>Options for the <c>storage</c> namespace; must inherit the global options type.</summary>
public sealed class StorageCommandNamespaceOptions : GlobalCliOptions
{
	/// <summary>Prefix filter for listings.</summary>
	public string Prefix { get; set; } = "";
}
