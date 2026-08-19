<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/argh-lockup.svg"/>
    <source media="(prefers-color-scheme: light)" srcset="docs/images/argh-lockup-light.svg"/>
    <img src="docs/images/argh-lockup.svg" alt="--argh_" width="174" height="96"/>
  </picture>
</p>

# Nullean.Argh

[![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.svg)](https://www.nuget.org/packages/Nullean.Argh)
[![CI](https://github.com/nullean/argh/actions/workflows/ci.yml/badge.svg)](https://github.com/nullean/argh/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Build full-featured .NET CLIs without writing a parser.

Methods become commands, XML docs become help text, records become option sets. A Roslyn source generator emits parsing, routing, dispatch, and help into your assembly at build time — no reflection, no runtime overhead, trimming- and AOT-safe by default.

Write vanilla C# and get a fully functional CLI in return: rich `--help` output, shell tab-completions for bash, zsh, and fish, and a machine-readable JSON schema ready for agentic use cases — all without writing a single line of plumbing code for any of it.

***Heavily** Inspired by [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) (Cysharp) — rewritten from scratch with a different feature set, but ConsoleAppFramework laid out the path for source-generated CLI's in .NET.*

![Sample CLI help output (XmlDocShowcase)](https://cdn.jsdelivr.net/gh/nullean/argh@main/docs/assets/xml-doc-showcase-help.gif)

## Documentation

**[Full documentation →](https://nullean.github.io/argh/)**

## Features

- **XML docs are your help text** — summaries, param descriptions, remarks, and `<example>` blocks appear in `--help` automatically
- **Everything is generated C#** — typed dispatch tree, option parsers, and help printers emitted directly into your assembly
- **`MapGroup`-style namespaces** — nested command groups with their own help pages and scoped option types
- **DTO binding with `[AsParameters]`** — records and classes expand into flags without a custom bind loop
- **Shell completions built-in** — bash, zsh, fish; one install command per shell
- **Agent-ready schema** — `myapp __schema` emits full JSON; conforms to [cli-schema v1](https://github.com/cli-schema/cli-schema)
- **Fuzzy matching** — typos produce actionable errors with suggestions
- **DataAnnotations validation** — `[Range]`, `[StringLength]`, `[AllowedValues]`, `[Existing]`, and more — constraints in `--help`, failures exit 2
- **CancellationToken injection** — add it to a handler, it tracks Ctrl+C (or host shutdown with Hosting)
- **Zero-dep or ME.* native** — `Nullean.Argh` has no `Microsoft.Extensions.*` dependency; `Nullean.Argh.Hosting` plugs into `IHost` and DI

## Packages

| Package | NuGet | Description |
|---------|-------|-------------|
| [`Nullean.Argh`](src/Nullean.Argh/README.md) | [![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.svg)](https://www.nuget.org/packages/Nullean.Argh) | Metapackage for console apps (Core + Interfaces) |
| [`Nullean.Argh.Hosting`](src/Nullean.Argh.Hosting/README.md) | [![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Hosting.svg)](https://www.nuget.org/packages/Nullean.Argh.Hosting) | Microsoft.Extensions.Hosting integration |
| [`Nullean.Argh.Core`](src/Nullean.Argh.Core/README.md) | [![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Core.svg)](https://www.nuget.org/packages/Nullean.Argh.Core) | Shared runtime + embedded source generator (transitive) |
| [`Nullean.Argh.Interfaces`](src/Nullean.Argh.Interfaces/README.md) | [![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Interfaces.svg)](https://www.nuget.org/packages/Nullean.Argh.Interfaces) | Contracts and attributes (transitive, or for shared libs) |

**Which package do I need?**

- [`Nullean.Argh`](https://www.nuget.org/packages/Nullean.Argh) — dependency-free console apps
- [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) — apps using `Microsoft.Extensions.Hosting` / DI

Everything else is pulled in transitively.

## Quick start

### Console app

```xml
<PackageReference Include="Nullean.Argh" />
```

```csharp
using Nullean.Argh;

var app = new ArghApp();
app.Map("hello", MyHandlers.SayHello);

return await app.RunAsync(args);
```

### Hosted app

```xml
<PackageReference Include="Nullean.Argh.Hosting" />
```

```csharp
using Microsoft.Extensions.Hosting;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArgh(args, b =>
{
    b.Map("hello", MyHandlers.SayHello);
});

await builder.Build().RunAsync();
```

## Registration model

```csharp
// Method group — direct typed dispatch
app.Map("deploy", DeployHandlers.Run);

// Lambda — one-liners
app.Map("greet", (string name) => Console.WriteLine($"Hello, {name}!"));

// Class — every public method becomes a command
app.Map<StorageHandlers>();

// Namespaces — nested command groups
app.MapNamespace<StorageCommands>("storage", ns =>
{
    ns.MapNamespace<BlobCommands>("blob");
});
```

## Parameters

```csharp
// Positional arguments
public static Task<int> Deploy([Argument] string environment) { … }

// Named flags (auto kebab-case)
public static Task<int> Build(string outputDir, bool release = false) { … }
// → --output-dir ./bin --release

// DTO binding
public record DeployOptions(string Environment, bool DryRun = false);
public static Task<int> Deploy([AsParameters] DeployOptions opts) { … }

// Validation
public static void Run(
    [Range(1, 65535)] int port,
    [AllowedValues("dev", "staging", "prod")] string env) { … }
```

## Shell completions

```bash
eval "$(myapp __completion bash)"    # bash
source <(myapp __completion zsh)     # zsh
myapp __completion fish > ~/.config/fish/completions/myapp.fish  # fish
```

## Schema

```bash
myapp __schema > cli-schema.json
```

Emits a full JSON description conforming to [cli-schema v1](https://github.com/cli-schema/cli-schema) — feed it to an LLM, generate docs, or diff in CI.

## Examples

See [`examples/`](examples/) for complete working projects:

- [`Basic`](examples/Basic/) — minimal console app
- [`Hosted`](examples/Hosted/) — Microsoft.Extensions.Hosting
- [`HostedRoot`](examples/HostedRoot/) — hosted with root command
- [`XmlDocShowcase`](examples/XmlDocShowcase/) — full XML doc tag inventory
- [`ArghAotSmoketest`](examples/ArghAotSmoketest/) — Native AOT validation

## License

[MIT](LICENSE)
