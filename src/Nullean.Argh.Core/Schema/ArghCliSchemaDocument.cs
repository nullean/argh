namespace Nullean.Argh.Schema;

/// <summary>Root document for Argh CLI JSON schema export (<c>__schema</c> / <see cref="Runtime.ArghRuntime.FormatCliSchemaJson"/>).</summary>
public sealed record ArghCliSchemaDocument(
	int SchemaVersion,
	string EntryAssembly,
	string Version,
	string? Description,
	string[] ReservedMetaCommands,
	CliParameterSchema[] GlobalOptions,
	CliDefaultHandlerSchema? RootDefault,
	CliCommandSchema[] Commands,
	CliNamespaceSchema[] Namespaces);

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
	CliParameterSchema[] Parameters);

/// <summary>Root or namespace default handler (no argv token).</summary>
public sealed record CliDefaultHandlerSchema(
	string Kind,
	string? Summary,
	string? Notes,
	string? Usage,
	string[] Examples,
	CliParameterSchema[] Parameters);

/// <summary>CLI flag or positional parameter description.</summary>
/// <param name="Role"><c>flag</c> or <c>positional</c>.</param>
public sealed record CliParameterSchema(
	string Role,
	string Name,
	string? ShortName,
	string Kind,
	bool Required,
	string? Summary,
	CliConstraintSchema[]? Validations = null);

/// <summary>A single validation constraint on a CLI parameter.</summary>
/// <param name="Kind">One of: range, length, regex, allowed, denied, email, url, timeSpanRange.</param>
public sealed record CliConstraintSchema(
	string Kind,
	string? Min = null,
	string? Max = null,
	string? Pattern = null,
	string[]? Values = null);
