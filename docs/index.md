# Nullean.Argh

Build full-featured .NET CLIs without writing a parser.

A Roslyn source generator turns plain C# methods into a complete CLI at build time. No reflection, no runtime overhead, trimming- and AOT-safe by default.

## See it in action

This single file produces a CLI with namespaces, typed flags, validation, shell completions, JSON schema, and rich `--help` output:

```csharp
using Nullean.Argh;
using System.ComponentModel.DataAnnotations;

var app = new ArghApp();

app.UseGlobalOptions<GlobalOptions>();
app.Map<DeployCommands>();
app.MapNamespace<StorageCommands>("storage", ns =>
{
    ns.MapNamespace<BlobCommands>("blob");
});

return await app.RunAsync(args);

public record GlobalOptions(bool Verbose = false);

public static class DeployCommands
{
    /// <summary>Deploy the app to a target environment.</summary>
    /// <param name="environment">Target environment.</param>
    /// <param name="dryRun">Validate only, make no changes.</param>
    /// <param name="port">-p, Service port.</param>
    public static async Task<int> Deploy(
        [Argument] string environment,
        [Range(1, 65535)] int port = 8080,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        Console.WriteLine($"Deploying to {environment}:{port}");
        return 0;
    }
}

/// <summary>Manage cloud storage resources.</summary>
public sealed class StorageCommands
{
    /// <summary>List objects in the bucket.</summary>
    public void List() => Console.WriteLine("listing...");
}

/// <summary>Blob sub-commands.</summary>
public sealed class BlobCommands
{
    /// <summary>Upload a file to storage.</summary>
    /// <param name="path">-p, --path, Local file path.</param>
    public void Upload([Existing] FileInfo path) =>
        Console.WriteLine($"uploading {path.Name}");
}
```

What you get from this:

```
$ myapp deploy --help
Usage: myapp deploy <environment> [options]

  Deploy the app to a target environment.

Arguments:
  <environment>       Target environment.

Options:
  -p, --port <int>    Service port. [default: 8080] [range: 1-65535]
  --dry-run           Validate only, make no changes.

Global options:
  -h, --help          Show help.
  --verbose

$ myapp storage blob upload --path ~/missing.txt
Error: --path: file does not exist.

$ myapp storag list
Error: unknown command 'storag'. Did you mean 'storage'?

$ myapp __schema > cli-schema.json   # full JSON schema for agents/docs/CI
$ eval "$(myapp __completion bash)"  # tab completions installed
```

## Features

Every feature below is generated at compile time. Nothing runs via reflection.

- **[XML docs are your help text](features/help.md)** - `<summary>`, `<param>`, `<remarks>`, and `<example>` flow directly into `--help`
- **[Source-generated](features/source-generated.md)** - typed dispatch, parsers, and help printers emitted into your assembly
- **[Shell completions](features/completions.md)** - bash, zsh, fish; one install command per shell
- **[Agent-ready JSON schema](features/schema.md)** - `__schema` emits a full CLI description conforming to [cli-schema v1](https://github.com/cli-schema/cli-schema)
- **[DataAnnotations validation](features/validation.md)** - `[Range]`, `[StringLength]`, `[Existing]`, and more; constraints shown in help
- **[Fuzzy matching](features/fuzzy-matching.md)** - typos produce suggestions with the correct qualified path
- **[DTO binding](features/dto-binding.md)** - `[AsParameters]` expands records/classes into flags automatically
- **[Middleware](features/middleware.md)** - cross-cutting logic (timing, auth, logging) without touching handlers
- **[Cancellation](features/cancellation.md)** - add `CancellationToken` to any handler; tracks Ctrl+C or host shutdown
- **[AOT and trimming safe](features/aot.md)** - zero reflection, ships trimmed or native-compiled

## Install

::::{tab-set}

:::{tab-item} Console app
```xml
<PackageReference Include="Nullean.Argh" />
```
:::

:::{tab-item} Hosted app
```xml
<PackageReference Include="Nullean.Argh.Hosting" />
```
:::

::::

## Packages

| Package | Description |
|---------|-------------|
| [`Nullean.Argh`](https://www.nuget.org/packages/Nullean.Argh) | Dependency-free console apps |
| [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) | Microsoft.Extensions.Hosting integration |
| [`Nullean.Argh.Core`](https://www.nuget.org/packages/Nullean.Argh.Core) | Shared runtime + embedded generator (transitive) |
| [`Nullean.Argh.Interfaces`](https://www.nuget.org/packages/Nullean.Argh.Interfaces) | Contracts and attributes for shared libraries |

:::{tip}
Most apps only need `Nullean.Argh` or `Nullean.Argh.Hosting`. Everything else is pulled in transitively.
:::
