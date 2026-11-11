<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/argh-lockup.svg">
    <source media="(prefers-color-scheme: light)" srcset="docs/images/argh-lockup-light.svg">
    <img src="docs/images/argh-lockup.svg" width="174" height="96" alt="Nullean.Argh">
  </picture>
</p>

<p align="center">Build full-featured .NET CLIs without writing a parser: methods become commands, XML docs become help text.</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Nullean.Argh"><img src="https://img.shields.io/nuget/v/Nullean.Argh?style=flat-square" alt="NuGet"></a>
  <a href="https://github.com/nullean/argh/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/nullean/argh/ci.yml?branch=main&style=flat-square&label=ci" alt="CI"></a>
  <a href="https://argh.nullean.net"><img src="https://img.shields.io/badge/docs-argh.nullean.net-20a0a0?style=flat-square" alt="Docs"></a>
</p>

![Sample CLI help output (XmlDocShowcase)](docs/assets/xml-doc-showcase-help.gif)

Methods become commands, XML docs become help text, records become option sets. A Roslyn source generator emits
parsing, routing, dispatch and help into your assembly at build time: no reflection, no runtime overhead, trimming- and
AOT-safe by default.

Write vanilla C# and get a fully functional CLI in return: rich `--help` output, shell tab-completions for bash, zsh and
fish, and a machine-readable JSON schema ready for agentic use cases, all without writing a line of plumbing code.

***Heavily** inspired by [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) (Cysharp): rewritten
from scratch with a different feature set, but ConsoleAppFramework laid out the path for source-generated CLIs in .NET.*

## Install

```sh
dotnet add package Nullean.Argh
```

For an app built on `Microsoft.Extensions.Hosting` and DI:

```sh
dotnet add package Nullean.Argh.Hosting
dotnet add package Microsoft.Extensions.Hosting
```

## Quick start

### Console app

```csharp
using Nullean.Argh;

var app = new ArghApp();
app.Map("hello", Handlers.Hello);

return await app.RunAsync(args);

static class Handlers
{
	/// <summary>Greets someone by name.</summary>
	/// <param name="name">-n,--name, Name to greet.</param>
	public static void Hello(string name) => Console.WriteLine($"Hello, {name}!");
}
```

`dotnet run -- hello --name Argh` prints `Hello, Argh!`, and `dotnet run -- hello --help` prints the XML docs as help.

### Hosted app

```csharp
using Microsoft.Extensions.Hosting;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArgh(args, b =>
{
	b.Map("hello", Handlers.Hello);
});

await builder.Build().RunAsync();
```

## Features

- **XML docs are your help text**: summaries, param descriptions, remarks and `<example>` blocks appear in `--help` automatically.
- **Everything is generated C#**: typed dispatch tree, option parsers and help printers emitted directly into your assembly.
- **`MapGroup`-style namespaces**: nested command groups with their own help pages and scoped option types.
- **DTO binding with `[AsParameters]`**: records and classes expand into flags without a custom bind loop.
- **Shell completions built in**: bash, zsh, fish; one install command per shell.
- **Agent-ready schema**: `myapp __schema` emits full JSON, conforming to [cli-schema v1](https://github.com/cli-schema/cli-schema).
- **Fuzzy matching**: typos produce actionable errors with suggestions.
- **DataAnnotations validation**: `[Range]`, `[StringLength]`, `[AllowedValues]`, `[Existing]` and more; constraints show in `--help`, failures exit 2.
- **CancellationToken injection**: add it to a handler and it tracks Ctrl+C (or host shutdown with Hosting).
- **Zero-dep or ME.\* native**: `Nullean.Argh` has no `Microsoft.Extensions.*` dependency; `Nullean.Argh.Hosting` plugs into `IHost` and DI.

### Registration model

```csharp
// Method group: direct typed dispatch
app.Map("deploy", DeployHandlers.Run);

// Lambda: one-liners
app.Map("greet", (string name) => Console.WriteLine($"Hello, {name}!"));

// Class: every public method becomes a command
app.Map<StorageHandlers>();

// Namespaces: nested command groups
app.MapNamespace<StorageCommands>("storage", ns =>
{
	ns.MapNamespace<BlobCommands>("blob");
});
```

### Parameters

```csharp
using System.ComponentModel.DataAnnotations;
using Nullean.Argh;

public record DeployOptions(string Environment, bool DryRun = false);

public static class Commands
{
	// Positional arguments
	public static void Deploy([Argument] string environment) => Console.WriteLine(environment);

	// Named flags, kebab-cased: --output-dir ./bin --release
	public static void Build(string outputDir, bool release = false) => Console.WriteLine($"{outputDir} {release}");

	// DTO binding: --environment prod --dry-run
	public static void Ship([AsParameters] DeployOptions opts) => Console.WriteLine(opts);

	// Validation
	public static void Run(
		[Range(1, 65535)] int port,
		[AllowedValues("dev", "staging", "prod")] string env) => Console.WriteLine($"{env}:{port}");
}
```

### Shell completions

```bash
eval "$(myapp __completion bash)"                                # bash
source <(myapp __completion zsh)                                 # zsh
myapp __completion fish > ~/.config/fish/completions/myapp.fish  # fish
```

### Schema

```bash
myapp __schema > cli-schema.json
```

Emits a full JSON description conforming to [cli-schema v1](https://github.com/cli-schema/cli-schema): feed it to an
LLM, generate docs, or diff it in CI.

## Packages

| Package | What it is | |
|---|---|---|
| [`Nullean.Argh`](src/Nullean.Argh/README.md) | metapackage for console apps (Core + Interfaces) | [![NuGet](https://img.shields.io/nuget/v/Nullean.Argh?style=flat-square)](https://www.nuget.org/packages/Nullean.Argh) |
| [`Nullean.Argh.Hosting`](src/Nullean.Argh.Hosting/README.md) | `Microsoft.Extensions.Hosting` and DI integration | [![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Hosting?style=flat-square)](https://www.nuget.org/packages/Nullean.Argh.Hosting) |
| [`Nullean.Argh.Core`](src/Nullean.Argh.Core/README.md) | shared runtime and embedded source generator (transitive) | [![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Core?style=flat-square)](https://www.nuget.org/packages/Nullean.Argh.Core) |
| [`Nullean.Argh.Interfaces`](src/Nullean.Argh.Interfaces/README.md) | contracts and attributes (transitive, or for shared libraries) | [![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Interfaces?style=flat-square)](https://www.nuget.org/packages/Nullean.Argh.Interfaces) |
| [`Nullean.Make`](src/Nullean.Make/README.md) | typed build targets for .NET, with Argh's help | [![NuGet](https://img.shields.io/nuget/v/Nullean.Make?style=flat-square)](https://www.nuget.org/packages/Nullean.Make) |
| [`Nullean.Make.Fs`](src/Nullean.Make.Fs/README.md) | F# build targets as DU cases over Nullean.Make | [![NuGet](https://img.shields.io/nuget/v/Nullean.Make.Fs?style=flat-square)](https://www.nuget.org/packages/Nullean.Make.Fs) |

Which package do I need? `Nullean.Argh` for dependency-free console apps, `Nullean.Argh.Hosting` for apps using
`Microsoft.Extensions.Hosting` and DI. Everything else is pulled in transitively.

## Learn more

- Documentation: https://argh.nullean.net
- [`examples/`](examples/), complete working projects:
  [`Basic`](examples/Basic/) (minimal console app),
  [`Hosted`](examples/Hosted/) (Microsoft.Extensions.Hosting),
  [`HostedRoot`](examples/HostedRoot/) (hosted with a root command),
  [`XmlDocShowcase`](examples/XmlDocShowcase/) (full XML doc tag inventory),
  [`ArghAotSmoketest`](examples/ArghAotSmoketest/) (Native AOT validation),
  [`MakeBuild`](examples/MakeBuild/) (a Nullean.Make build)
- [Releases](https://github.com/nullean/argh/releases)
- [Contributing](https://github.com/nullean/.github/blob/main/CONTRIBUTING.md)

MIT licensed. Copyright Nullean and contributors.
