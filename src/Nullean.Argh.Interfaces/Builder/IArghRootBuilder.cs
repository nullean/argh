using Nullean.Argh.Documentation;

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

	/// <summary>
	/// Documents environment variables and optional configuration files the program reads.
	/// Analyzed by the source generator and emitted as the <c>environment</c> object in <c>__schema</c> output.
	/// Arguments must be <c>new CliEnvVarDoc(...)</c> or <c>new CliConfigFileDoc(...)</c> object creation
	/// expressions with string/bool literals so the generator can extract them statically.
	/// </summary>
	IArghRootBuilder DocumentEnvironmentVariables(
		CliEnvVarDoc[]? variables = null,
		CliConfigFileDoc[]? configFiles = null);
}
