namespace Basic;

/// <summary>Flags parsed before routing; available to every command and group.</summary>
public class GlobalCliOptions
{
	/// <summary>Print extra diagnostics from filters and handlers.</summary>
	public bool Verbose { get; set; }
}
