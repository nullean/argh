# DTO binding with `[AsParameters]`

A record or class parameter annotated with `[AsParameters]` expands its members into individual flags or positionals. Works with **records** (constructor parameters) and **classes** (public settable properties). Add a string argument to prefix all long names.

## Record binding

Constructor parameters become flags:

```csharp
public record DeployOptions(string Environment, bool DryRun = false);

public static Task<int> Deploy([AsParameters] DeployOptions opts) { … }
// myapp deploy --environment staging --dry-run
```

## Class binding

Public settable properties become flags:

```csharp
public class BuildOptions
{
    public string OutputDir { get; set; } = "";
    public bool Release { get; set; }
}

public static Task<int> Build([AsParameters] BuildOptions opts) { … }
// myapp build --output-dir ./bin --release
```

## Prefix support

All long names get a common prefix when you pass a string argument:

```csharp
public record AppOptions(string Name, string Version = "");
public static Task<int> Configure([AsParameters("app")] AppOptions opts) { … }
// myapp configure --app-name foo --app-version 2
```

## Long name override on DTOs

The same XML doc override syntax works on `[AsParameters]` properties, fields, and primary-constructor parameters via their `<summary>` or `<param>` doc lines:

```csharp
/// <param name="outputDir">-o, --out, --output, Output directory.</param>
public record BuildOptions(string OutputDir);
```

## Combining with options types

A command can extend a global or namespace options type and annotate it with `[AsParameters]` to inherit those flags alongside its own:

```csharp
public record DeployOptions(string Environment, bool DryRun = false) : StorageOptions;

ns.Map("deploy", ([AsParameters] DeployOptions opts) => { … });
// myapp storage deploy --connection-string "…" --environment staging --dry-run
```

## Cross-assembly DTOs

:::{important}
If an `[AsParameters]` DTO lives in a **separate project** from the CLI entry point, the generator cannot access its source syntax at analysis time. It falls back to loading the companion `.xml` documentation file from disk.

The DTO project **must** enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>` for short-alias declarations and member descriptions to flow into `--help` output and short-flag parsing.
:::

Handler parameters and `[AsParameters]` types defined in the **same project** as the CLI are always resolved from source and are not affected.
