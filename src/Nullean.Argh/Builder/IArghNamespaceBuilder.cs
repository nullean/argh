namespace Nullean.Argh.Builder;

/// <summary>
/// <see cref="IArghBuilder"/> for an <see cref="ArghApp.AddNamespace{T}(string,System.Action{IArghNamespaceBuilder})"/> configure callback,
/// exposing the current namespace segment (e.g. for <c>storage</c> in <c>app storage …</c>).
/// </summary>
public interface IArghNamespaceBuilder : IArghBuilder
{
	/// <inheritdoc cref="ArghApp.AddNamespace{T}(string)"/>
	new IArghNamespaceBuilder AddNamespace<T>(string name) where T : class;

	/// <summary>Path segment for this namespace (not the full <c>a/b/c</c> path).</summary>
	string Segment { get; }
}
