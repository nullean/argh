namespace Nullean.Argh.Schema;

/// <summary>Root document for Argh CLI JSON schema export (<c>__schema</c> / <see cref="Runtime.ArghRuntime.FormatCliSchemaJson"/>).</summary>
public sealed record ArghCliSchemaDocument(
	int SchemaVersion,
	string Name,
	string Version,
	string? Description,
	string[] ReservedMetaCommands,
	CliParameterSchema[] GlobalOptions,
	CliDefaultHandlerSchema? RootDefault,
	CliCommandSchema[] Commands,
	CliNamespaceSchema[] Namespaces,
	string[]? Tags = null,
	bool? RequiresAuth = null,
	string[]? AuthCommands = null,
	CliEnvironmentSchema? Environment = null);

/// <summary>Nested command namespace (subcommand group).</summary>
public sealed record CliNamespaceSchema(
	string Segment,
	string? Summary,
	string? Notes,
	CliParameterSchema[] Options,
	CliDefaultHandlerSchema? DefaultCommand,
	CliCommandSchema[] Commands,
	CliNamespaceSchema[] Namespaces);

/// <summary>Registered command (non-default handler).</summary>
public sealed record CliCommandSchema(
	string[] Path,
	string Name,
	string? Summary,
	string? Notes,
	string? Usage,
	string[] Examples,
	CliParameterSchema[] Parameters,
	string[]? Aliases = null,
	bool Hidden = false,
	string[]? Tags = null,
	CliDeprecationSchema? Deprecated = null,
	CliIntentSchema? Intent = null,
	CliOutputSchema? Output = null,
	bool Streaming = false,
	bool LongRunning = false);

/// <summary>Side-effect profile of a command, for agent reasoning.</summary>
public sealed record CliIntentSchema(
	bool? Destructive = null,
	bool? Idempotent = null,
	string? Scope = null,
	bool? RequiresConfirmation = null,
	bool? RequiresAuth = null);

/// <summary>Machine-readable output format declarations for a command.</summary>
public sealed record CliOutputSchema(
	string[]? Formats = null,
	string? FormatFlag = null);

/// <summary>Root or namespace default handler (no argv token).</summary>
public sealed record CliDefaultHandlerSchema(
	string Kind,
	string? Summary,
	string? Notes,
	string? Usage,
	string[] Examples,
	CliParameterSchema[] Parameters,
	bool Hidden = false);

/// <summary>CLI flag or positional parameter description.</summary>
/// <param name="Role"><c>flag</c>, <c>positional</c>, <c>confirmationSkip</c>, or <c>dryRun</c>.</param>
/// <param name="Type">JSON Schema primitive: <c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>, <c>array</c>, or <c>enum</c>.</param>
public sealed record CliParameterSchema(
	string Role,
	string Name,
	string? ShortName,
	string Type,
	bool Required,
	string? Summary,
	string? DefaultValue = null,
	bool Repeatable = false,
	string? Separator = null,
	string[]? Aliases = null,
	string[]? EnumValues = null,
	string? ElementType = null,
	bool Hidden = false,
	bool Variadic = false,
	CliDeprecationSchema? Deprecated = null,
	CliConstraintSchema[]? Validations = null);

/// <summary>A single validation constraint on a CLI parameter.</summary>
/// <param name="Kind">One of: range, length, count, regex, allowed, denied, email, url, uriScheme, fileExtensions, timeSpanRange, existing, nonExisting, rejectSymbolicLinks, expandUserProfile.</param>
public sealed record CliConstraintSchema(
	string Kind,
	string? Min = null,
	string? Max = null,
	string? Pattern = null,
	string[]? Values = null);

/// <summary>
/// Structured deprecation metadata for a command or parameter. Serialized by <see cref="CliSchemaJsonWriter"/>
/// as <c>true</c> when no structured details are present, or as an object with non-null fields when details
/// exist, matching the cli-schema v1 deprecation oneOf.
/// </summary>
public sealed record CliDeprecationSchema(
	string? Message = null,
	string? Since = null,
	string? RemovedIn = null);

/// <summary>External context the program depends on (env vars and config files).</summary>
public sealed record CliEnvironmentSchema(
	CliEnvVarSchema[]? Variables = null,
	CliConfigFileSchema[]? ConfigFiles = null);

/// <summary>An environment variable the program reads.</summary>
public sealed record CliEnvVarSchema(
	string Name,
	string? Description = null,
	bool Required = false,
	string? DefaultValue = null);

/// <summary>A configuration file the program reads.</summary>
public sealed record CliConfigFileSchema(
	string Path,
	string? Description = null,
	bool Required = false);
