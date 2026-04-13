namespace Nullean.Argh;

/// <summary>
/// Populated by the source generator for <c>AddNamespace&lt;T&gt;(Action&lt;IArghNamespaceBuilder&gt;)</c> (no explicit segment string).
/// Uses generic static fields only (AOT-friendly, no reflection).
/// </summary>
public static class ArghNamespaceSegmentCodegen
{
	private static class Holder<T> where T : class
	{
		internal static string? Value;
	}

	/// <summary>For source generator use: records the CLI segment for handler type <typeparamref name="T"/>.</summary>
	public static void Set<T>(string segment) where T : class =>
		Holder<T>.Value = segment;

	internal static string? Get<T>() where T : class =>
		Holder<T>.Value;
}
