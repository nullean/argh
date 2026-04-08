namespace Hosted;

/// <summary>Global flags (shared with the Basic example).</summary>
public class HostedGlobalCliOptions
{
	/// <summary>Extra diagnostics.</summary>
	public bool Verbose { get; set; }
}

/// <summary>Group options for <c>storage</c>; inherits global options.</summary>
public class HostedStorageGroupOptions : HostedGlobalCliOptions
{
	/// <summary>Optional key prefix.</summary>
	public string Prefix { get; set; } = "";
}

public sealed class UploadOptions : HostedStorageGroupOptions
{
	/// <summary> The target to upload to. </summary>
	public string? Target { get; set; }
}
