namespace Nullean.Argh.Help;

/// <summary>
/// Writes shell completion candidates to <see cref="Console.Out"/>, one per line.
/// </summary>
public static class CompletionWriter
{
	/// <summary>
	/// Writes every candidate that matches <paramref name="prefix"/> (prefix filter; empty prefix matches all).
	/// Candidates are ordered ordinally for stable output.
	/// </summary>
	public static void WriteFiltered(ReadOnlySpan<string> candidates, string prefix)
	{
		if (candidates.Length == 0)
			return;
		var arr = new string[candidates.Length];
		candidates.CopyTo(arr);
		Array.Sort(arr, StringComparer.Ordinal);
		foreach (var c in arr)
		{
			if (prefix.Length == 0 || c.StartsWith(prefix, StringComparison.Ordinal))
				Console.Out.WriteLine(c);
		}
	}
}
