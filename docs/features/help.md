---
title: Help
---

# Help and XML documentation

Write XML doc once; the generator reads it at build time and bakes the text into `--help` output. No `.xml` doc file is read at runtime. The generator accesses doc comments through the Roslyn compilation model.

:::{tip}
`GenerateDocumentationFile` is **not required** for the usual developer inner loop. The generator resolves doc comments directly from source.
:::

## Command help

Document handler methods normally:

```csharp
/// <summary>Deploy the application to the target environment.</summary>
/// <remarks>
/// Runs pre-flight checks before deploying. Pass <c>--dry-run</c> to
/// validate without making changes. See also <see cref="Rollback"/>.
/// </remarks>
/// <param name="environment">Target environment (staging, production).</param>
/// <param name="dryRun">Validate only - make no changes.</param>
public static Task<int> Deploy(string environment, bool dryRun = false) { … }
```

The generated `myapp deploy --help` output:

```
Usage: myapp deploy <environment> [options]

   Deploy the application to the target environment.

Global options:
  -h, --help              Show help.

Arguments:
  <environment>           Target environment (staging, production).

Options:
  --dry-run               Validate only - make no changes.

Notes:
  Runs pre-flight checks before deploying. Pass --dry-run to validate
  without making changes. See also: myapp rollback <args>
```

## Namespace help

Put the `<summary>` (and optionally `<remarks>`) on the class `T` passed to `AddNamespace<T>`. The generator uses it as the namespace description in `myapp storage --help` and in the root command listing.

```csharp
/// <summary>Manage blob and file storage resources.</summary>
/// <remarks>
/// Requires a storage connection string via <c>--connection-string</c>
/// or the <c>STORAGE_CONN</c> environment variable.
/// </remarks>
internal sealed class StorageCommands { … }

app.AddNamespace<StorageCommands>("storage", ns => { … });
```

## Root app help

The root `myapp --help` shows a description in two ways:

- **`UseCliDescription`** - for apps with no default root command, sets a one-liner shown beneath `Usage:`
- **`MapRoot`** - when you also want a default handler, put the XML doc on that handler method

:::{dropdown} XML tag rendering
In **remarks**, the following tags are rendered:

- `<paramref name="flag">` → `--flag` (kebab-case long name)
- `<see cref="OtherHandler"/>` → that command's usage synopsis
- `<c>text</c>` → literal text
- `<example>` blocks appear in help output
:::

## Cross-assembly DTO types

If an `[AsParameters]` DTO lives in a **separate project** from the CLI entry point, the generator cannot access its source syntax at analysis time. It falls back to loading the companion `.xml` documentation file.

:::{warning}
The DTO project **must** enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>` for short-alias declarations and member descriptions to flow into `--help`.
:::

Handler parameters and `[AsParameters]` types defined in the **same project** as the CLI are always resolved from source and are not affected.

## Test projects

If a **test** assembly uses `InternalsVisibleTo` to see internal members of a referenced CLI project, the generator emits a stable, per-assembly generated root type name so `CS0436` collisions should not occur.
