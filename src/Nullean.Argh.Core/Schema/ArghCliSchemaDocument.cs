using System.Text.Json.Serialization;

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
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string[]? Tags = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? RequiresAuth = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string[]? AuthCommands = null);

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
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string[]? Aliases = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	bool Hidden = false,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string[]? Tags = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	CliIntentSchema? Intent = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	CliOutputSchema? Output = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	bool Streaming = false,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	bool LongRunning = false);

/// <summary>Side-effect profile of a command, for agent reasoning.</summary>
public sealed record CliIntentSchema(
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? Destructive = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? Idempotent = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Scope = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? RequiresConfirmation = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? RequiresAuth = null);

/// <summary>Machine-readable output format declarations for a command.</summary>
public sealed record CliOutputSchema(
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string[]? Formats = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? FormatFlag = null);

/// <summary>Root or namespace default handler (no argv token).</summary>
public sealed record CliDefaultHandlerSchema(
	string Kind,
	string? Summary,
	string? Notes,
	string? Usage,
	string[] Examples,
	CliParameterSchema[] Parameters,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	bool Hidden = false);

/// <summary>CLI flag or positional parameter description.</summary>
/// <param name="Role"><c>flag</c> or <c>positional</c>.</param>
/// <param name="Type">JSON Schema primitive: <c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>, <c>array</c>, or <c>enum</c>.</param>
public sealed record CliParameterSchema(
	string Role,
	string Name,
	string? ShortName,
	string Type,
	bool Required,
	string? Summary,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? DefaultValue = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	bool Repeatable = false,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Separator = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string[]? Aliases = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string[]? EnumValues = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? ElementType = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	bool Hidden = false,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	bool Variadic = false,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	CliConstraintSchema[]? Validations = null);

/// <summary>A single validation constraint on a CLI parameter.</summary>
/// <param name="Kind">One of: range, length, count, regex, allowed, denied, email, url, uriScheme, fileExtensions, timeSpanRange, existing, nonExisting, rejectSymbolicLinks, expandUserProfile.</param>
public sealed record CliConstraintSchema(
	string Kind,
	string? Min = null,
	string? Max = null,
	string? Pattern = null,
	string[]? Values = null);
