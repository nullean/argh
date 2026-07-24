---
title: Schema
---

# Schema JSON

`myapp __schema` writes a JSON document to stdout describing your entire CLI — commands, namespaces, global and namespace options, summaries, remarks, usage, and examples. The output is generated at build time from the same source the generator uses for routing and help, so it is always in sync with your code.

```bash
myapp __schema > cli-schema.json
```

## Use cases

- **LLM / agent tooling** — feed the schema to a language model to give it accurate, structured knowledge of your CLI's commands and options.
- **Generated documentation** — pipe into a docs generator or templating step to keep reference docs in sync without manual maintenance.
- **CI validation** — diff `cli-schema.json` across commits to catch unintentional breaking changes to the CLI surface.

## Schema shape

The shape is defined by `ArghCliSchemaDocument` and conforms to the [cli-schema v1 specification](https://github.com/cli-schema/cli-schema). Output is indented camelCase JSON. Reserved meta-commands (`__complete`, `__completion`, `__schema`) appear under `reservedMetaCommands`.

## Schema enrichment attributes

Add `using Nullean.Argh.Documentation;` to access the attributes below. They have no effect on parsing or validation — they only enrich the `__schema` output for agent tooling and documentation consumers.

### Side-effect profile (`intent` object)

```csharp
[CommandIntent(Intent.Destructive | Intent.RequiresConfirmation)]
[MutationScope(MutationScope.Global)]   // File | Directory | Global
[RequiresAuth]
public static Task Delete([ConfirmationSkip] bool yes = false, ...) { }
```

`Intent` is a flags enum — combine with `|`. `MutationScope.Global` means the command reaches beyond the local filesystem (cloud resources, databases, registries, etc.). `[RequiresAuth]` signals that an authenticated session is required. `[ConfirmationSkip]` on a flag parameter sets its schema `role` to `"confirmationSkip"` so agent consumers know to pass it automatically on destructive commands. `[DryRun]` sets `role` to `"dryRun"`.

### Output formats (`output` object)

```csharp
// Enum parameter — formats and formatFlag inferred automatically
public static void Report([CommandOutput] OutputFormat? format = null) { }

// Explicit format list on a string parameter
public static void Export([CommandOutput("json", "table")] string? fmt = null) { }
```

Place `[CommandOutput]` on the parameter (or `[AsParameters]` DTO property, or GlobalOptions property) that selects the output format. The flag name and format list are derived from the parameter — no extra arguments needed for enum types.

### Deprecated commands and parameters (`deprecated` object)

```csharp
[Obsolete("Use new-cmd instead.")]
public static void OldCmd(...) { }
```

`[Obsolete]` on a handler method or an `[AsParameters]` DTO property emits a `deprecated` object in the schema. The message, if provided, appears as `deprecated.message`.

### Environment variables and config files (`environment` object)

```csharp
builder.DocumentEnvironmentVariables(
    variables:
    [
        new CliEnvVar("GITHUB_TOKEN", Description: "GitHub API token", Required: true),
        new CliEnvVar("XDG_CONFIG_HOME", Description: "Config directory override"),
    ],
    configFiles:
    [
        new CliConfigFile("~/.config/myapp/config.json", Description: "Main config"),
    ]);
```

Arguments must be `new CliEnvVar(...)` / `new CliConfigFile(...)` object creation expressions with string/bool literals so the source generator can extract them statically.
