namespace External.Ns.Options;

/// <summary>GlobalOptions in a namespace unrelated to the consuming project — tests cross-assembly FQ name emission.</summary>
public class ExternalNsGlobalOptions
{
	/// <summary>-v</summary>
	public bool Verbose { get; set; }

	/// <summary>External tag.</summary>
	public string Tag { get; set; } = "default";
}
