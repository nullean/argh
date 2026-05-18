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
	/// Arguments must be <c>new CliEnvVar(...)</c> or <c>new CliConfigFile(...)</c> object creation
	/// expressions with string/bool literals so the generator can extract them statically.
	/// </summary>
	IArghRootBuilder DocumentEnvironmentVariables(
		CliEnvVar[]? variables = null,
		CliConfigFile[]? configFiles = null);

	/// <summary>
	/// Overrides the <c>version</c> string written into the <c>__schema</c> document.
	/// The argument MUST be a string literal — the source generator reads it at compile time
	/// and bakes it into the emitted schema factory. If unset, the schema version defaults to
	/// the major component of the entry assembly's identity version (e.g. <c>"1"</c>),
	/// so the generated source stays stable across patch/preview bumps.
	/// Does not affect the <c>--version</c> CLI flag, which always shows the full informational version.
	/// </summary>
	IArghRootBuilder UseSchemaVersion(string version);
}
