namespace Nullean.Argh;

/// <summary>
/// Edit-distance helpers for suggesting close matches (e.g. unknown command names). Uses the Levenshtein metric; no external dependencies.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class FuzzyMatch
{
	/// <summary>
	/// Computes the Levenshtein (edit) distance between <paramref name="a"/> and <paramref name="b"/>.
	/// </summary>
	/// <param name="a">First string.</param>
	/// <param name="b">Second string.</param>
	/// <returns>Minimum number of single-character insertions, deletions, or substitutions.</returns>
	public static int LevenshteinDistance(string a, string b)
	{
		if (a is null)
			throw new ArgumentNullException(nameof(a));
		if (b is null)
			throw new ArgumentNullException(nameof(b));

		if (a.Length == 0)
			return b.Length;
		if (b.Length == 0)
			return a.Length;

		var previous = new int[b.Length + 1];
		var current = new int[b.Length + 1];
		for (var j = 0; j <= b.Length; j++)
			previous[j] = j;

		for (var i = 1; i <= a.Length; i++)
		{
			current[0] = i;
			for (var j = 1; j <= b.Length; j++)
			{
				var cost = a[i - 1] == b[j - 1] ? 0 : 1;
				var del = previous[j] + 1;
				var ins = current[j - 1] + 1;
				var sub = previous[j - 1] + cost;
				current[j] = del < ins ? (del < sub ? del : sub) : (ins < sub ? ins : sub);
			}

			(previous, current) = (current, previous);
		}

		return previous[b.Length];
	}

	/// <summary>
	/// Returns every candidate whose Levenshtein distance to <paramref name="input"/> equals the smallest distance among those at most <paramref name="maxDistance"/>.
	/// </summary>
	/// <param name="input">User input to compare.</param>
	/// <param name="candidates">Possible matches; null entries are ignored.</param>
	/// <param name="maxDistance">Maximum edit distance to consider (inclusive).</param>
	/// <returns>All ties at the minimum distance within the cap, or an empty list when none qualify.</returns>
	public static IReadOnlyList<string> FindClosest(string input, IEnumerable<string> candidates, int maxDistance)
	{
		if (input is null)
			throw new ArgumentNullException(nameof(input));
		if (candidates is null)
			throw new ArgumentNullException(nameof(candidates));
		if (maxDistance < 0)
			throw new ArgumentOutOfRangeException(nameof(maxDistance));

		var best = new List<string>();
		var bestDistance = int.MaxValue;

		foreach (var candidate in candidates)
		{
			if (candidate is null)
				continue;

			var d = LevenshteinDistance(input, candidate);
			if (d > maxDistance)
				continue;

			if (d < bestDistance)
			{
				bestDistance = d;
				best.Clear();
				best.Add(candidate);
			}
			else if (d == bestDistance)
				best.Add(candidate);
		}

		return best;
	}
}
