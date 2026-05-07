using System.Text.Json.Serialization;

namespace Nullean.Argh.Schema;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ArghCliSchemaDocument))]
[JsonSerializable(typeof(CliNamespaceSchema))]
[JsonSerializable(typeof(CliCommandSchema))]
[JsonSerializable(typeof(CliDefaultHandlerSchema))]
[JsonSerializable(typeof(CliParameterSchema))]
[JsonSerializable(typeof(CliIntentSchema))]
[JsonSerializable(typeof(CliOutputSchema))]
[JsonSerializable(typeof(CliDeprecationSchema))]
[JsonSerializable(typeof(CliEnvironmentSchema))]
[JsonSerializable(typeof(CliEnvVarSchema))]
[JsonSerializable(typeof(CliConfigFileSchema))]
[JsonSerializable(typeof(CliEnvVarSchema[]))]
[JsonSerializable(typeof(CliConfigFileSchema[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(CliParameterSchema[]))]
[JsonSerializable(typeof(CliCommandSchema[]))]
[JsonSerializable(typeof(CliNamespaceSchema[]))]
internal partial class ArghCliSchemaJsonContext : JsonSerializerContext;
