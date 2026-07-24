---
title: Nullean.Argh
---

# Nullean.Argh

Build full-featured .NET CLIs without writing a parser.

Methods become commands, XML docs become help text, records become option sets. A Roslyn source generator emits parsing, routing, dispatch, and help into your assembly at build time — no reflection, no runtime overhead, trimming- and AOT-safe by default.

Write vanilla C# and get a fully functional CLI in return: rich `--help` output, shell tab-completions for bash, zsh, and fish, and a machine-readable JSON schema ready for agentic use cases — all without writing a single line of plumbing code for any of it.

## Key features

- **XML docs are your help text** — summaries, param descriptions, remarks, and `<example>` blocks appear in `--help` automatically
- **Everything is generated C#** — typed dispatch tree, option parsers, and help printers emitted directly into your assembly
- **`MapGroup`-style namespaces** — nested command groups with their own help pages and scoped option types
- **DTO binding with `[AsParameters]`** — records and classes expand into flags without a custom bind loop
- **Shell completions built-in** — generated lookup tables for subcommands, namespaces, and flags
- **Agent-ready schema** — `myapp __schema` emits a full JSON description for LLM and tooling consumption
- **Fuzzy matching** — typos produce actionable errors with suggestions
- **DataAnnotations validation** — annotate parameters with standard attributes, constraints appear in help
- **Zero-dep or ME.* native** — `Nullean.Argh` has no dependencies; `Nullean.Argh.Hosting` plugs into `IHost` and DI

## Quick install

**Console app** — no dependencies:

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh" />
</ItemGroup>
```

**Hosted app** — full `Microsoft.Extensions.*` integration:

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Hosting" />
</ItemGroup>
```

## Minimal example

```csharp
using Nullean.Argh;

var app = new ArghApp();
app.Map("hello", MyHandlers.SayHello);

return await app.RunAsync(args);
```

## Packages

| Package | Role |
|---------|------|
| [`Nullean.Argh`](https://www.nuget.org/packages/Nullean.Argh) | Dependency-free version for console apps. |
| [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) | Full integration with `Microsoft.Extensions.*` ecosystem. |
| [`Nullean.Argh.Core`](https://www.nuget.org/packages/Nullean.Argh.Core) | Shared runtime (pulled in transitively — not referenced directly). |
| [`Nullean.Argh.Interfaces`](https://www.nuget.org/packages/Nullean.Argh.Interfaces) | Reference directly only when building a shared library (reusable middleware or parsers). |

Everything else is pulled in transitively — you do not reference `.Core` or `.Interfaces` manually for normal apps.

**`Nullean.Argh.Generator`** is not a separate NuGet package — it ships embedded inside `Nullean.Argh.Core` under `analyzers/dotnet/cs`.

*Heavily inspired by [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) (Cysharp) — rewritten from scratch with a different feature set, but ConsoleAppFramework laid out the path for source-generated CLIs in .NET.*
