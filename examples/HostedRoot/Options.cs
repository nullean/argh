namespace HostedRoot;

/// <summary>Global flags.</summary>
public class HostedRootGlobalCliOptions
{
	/// <summary>Extra diagnostics.</summary>
	public bool Verbose { get; set; }
}

/// <summary>Options for the <c>storage</c> namespace; inherits global options.</summary>
public class HostedRootStorageNamespaceOptions : HostedRootGlobalCliOptions
{
	/// <summary>Optional key prefix.</summary>
	public string Prefix { get; set; } = "";
}
