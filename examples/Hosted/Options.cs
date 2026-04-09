namespace Hosted;

/// <summary>Global flags (shared with the Basic example).</summary>
public class HostedGlobalCliOptions
{
	/// <summary>Extra diagnostics.</summary>
	public bool Verbose { get; set; }
}

/// <summary>Command namespace options for <c>storage</c>; inherits global options.</summary>
public class HostedStorageCommandNamespaceOptions : HostedGlobalCliOptions
{
	/// <summary>Optional key prefix.</summary>
	public string Prefix { get; set; } = "";
}

public sealed class UploadOptions : HostedStorageCommandNamespaceOptions
{
	/// <summary> The target to upload to. </summary>
	public string? Target { get; set; }
}

/// <summary>Options for the <c>api</c> namespace.</summary>
public sealed class HostedApiNamespaceOptions : HostedGlobalCliOptions
{
	/// <summary>Optional API scope.</summary>
	public string Scope { get; set; } = "";
}
