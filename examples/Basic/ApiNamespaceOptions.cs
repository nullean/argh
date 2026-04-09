namespace Basic;

/// <summary>Options for the <c>api</c> namespace (inherits global options).</summary>
public sealed class ApiNamespaceOptions : GlobalCliOptions
{
	/// <summary>API key prefix for this group.</summary>
	public string ApiKey { get; set; } = "";
}
