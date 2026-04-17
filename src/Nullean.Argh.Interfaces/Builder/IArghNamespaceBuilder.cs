namespace Nullean.Argh.Builder;

/// <summary>
/// <see cref="IArghBuilder"/> for a <c>MapNamespace&lt;T&gt;(..., Action&lt;IArghNamespaceBuilder&gt;)</c> configure callback,
/// exposing the current namespace segment (e.g. for <c>storage</c> in <c>app storage …</c>).
/// </summary>
public interface IArghNamespaceBuilder : IArghBuilder
{
	/// <summary>Registers a nested namespace for handler type <typeparamref name="T"/> with an explicit segment name.</summary>
	new IArghNamespaceBuilder MapNamespace<T>(string name) where T : class;

	/// <summary>Path segment for this namespace (not the full <c>a/b/c</c> path).</summary>
	string Segment { get; }
}
