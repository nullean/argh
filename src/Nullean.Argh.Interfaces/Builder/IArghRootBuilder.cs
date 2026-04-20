namespace Nullean.Argh.Builder;

/// <summary>
/// Extends <see cref="IArghBuilder"/> with methods that are only meaningful at the application root (not inside a namespace configure callback).
/// </summary>
public interface IArghRootBuilder : IArghBuilder
{
	/// <summary>
	/// Sets a one-line description shown in root <c>--help</c> output, beneath the Usage line.
	/// Cannot be combined with <c>MapRoot</c>; the generator reports <c>AGH0023</c> if both are present.
	/// </summary>
	IArghRootBuilder UseCliDescription(string description);
}
