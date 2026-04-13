# Nullean.Argh

**Nullean.Argh is a .NET library for building CLIs** from plain C# methods: you register commands on [`ArghApp`](src/Nullean.Argh/ArghApp.cs), and a Roslyn analyzer emits the parser, router, help, and dispatch into your assembly. Binding is **source-generated**, **reflection-free** for your command surface, and **suitable for trimming / native AOT** (no generic runtime `Bind<T>()` over arbitrary types).

![Argh CLI help (root, `docs` namespace, `docs cross-refs`; ANSI colors, published XmlDocShowcase binary)](docs/assets/xml-doc-showcase-help.gif)

Recorded with [VHS](https://github.com/charmbracelet/vhs) (terminal output—including ANSI colors—is captured as-is). From the repo root: `dotnet publish examples/XmlDocShowcase/XmlDocShowcase.csproj -c Release -o docs/vhs/publish`, then `vhs < docs/vhs/xml-doc-showcase.tape`. The NuGet package readme cannot load relative images; use `https://raw.githubusercontent.com/nullean/argh/main/docs/assets/xml-doc-showcase-help.gif` for the same asset on nuget.org.

**Table of contents**

- [Acknowledgements](#acknowledgements)
- [Why this project](#why-this-project)
- [Packages](#packages)
- [Quick start](#quick-start)
- [Registration model](#registration-model)
- [Namespaces](#namespaces)
- [Global and namespace options](#global-and-namespace-options)
- [Parameters and binding](#parameters-and-binding)
- [Help and XML documentation](#help-and-xml-documentation)
- [Middleware](#middleware)
- [Dependency injection](#dependency-injection)
- [Hosting](#hosting)
- [Routing API](#routing-api)
- [User experience](#user-experience)
- [Shell completions](#shell-completions)
- [License and links](#license-and-links)

## Acknowledgements

[**ConsoleAppFramework**](https://github.com/Cysharp/ConsoleAppFramework) (Cysharp) is the **inspiration** and, in practice, the place that already **worked through the hard parts** of high-performance, source-generated CLI patterns (binding shape, codegen structure, and ecosystem expectations). The same “pregenerated binding” direction as [ConsoleAppFramework PR #237](https://github.com/Cysharp/ConsoleAppFramework/pull/237) applies here. **Nullean.Argh is not a fork**; it is a separate API and packaging choice that emits specialized C# per registration instead of a fully reflective parser.

## Why this project

- **Help**: Structured, colored help (`NO_COLOR`-aware), optional **XML documentation** from your assemblies in command help (see `GenerateDocumentationFile` and [`examples/XmlDocShowcase`](examples/XmlDocShowcase)).
- **Namespaces**: Nested command namespaces (think minimal `MapGroup`-style routing) with their own listings and options.
- **Options**: **Global** flags plus **per-namespace** option types with an inheritance chain enforced by the generator.
- **DTOs**: `[AsParameters]`-style binding of a record/class into many flags or positionals without a hand-written parser loop.
- **Hosting**: [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) wires the same fluent model into **`Microsoft.Extensions.DependencyInjection`** / **`IHost`**: `AddArgh`, lifetimes, `CancellationToken`, and `IHostApplicationLifetime` integration.

## Packages

| Package | Role |
|--------|------|
| [`Nullean.Argh`](https://www.nuget.org/packages/Nullean.Argh) | **Standalone CLI**: small runtime + embedded Roslyn analyzer (same package). Fully usable **without** a generic host—reference only this package for a dependency-light console app. |
| [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) | Optional layer with its own registration **DSL** ([`ArghHostingBuilder`](src/Nullean.Argh.Hosting/ArghHostingBuilder.cs)) that plugs the same fluent model **wholly** into **`Microsoft.Extensions.*`** (DI lifetimes, `IHost`, `IHostedService`, logging, cancellation). |

The analyzer ships inside `Nullean.Argh`; you do not reference `Nullean.Argh.Generator` separately.

**Base package reference**

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh" />
</ItemGroup>
```

**With Hosting** (add alongside or instead of wiring `ArghApp` only at the entrypoint—you still need `Nullean.Argh` for the analyzer/runtime types the DSL wraps):

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh" />
  <PackageReference Include="Nullean.Argh.Hosting" />
</ItemGroup>
```

## Quick start

### Base package (`Nullean.Argh`)

```csharp
using Nullean.Argh;

var app = new ArghApp();
app.Add("hello", MyHandlers.SayHello);

return await app.RunAsync(args);
```

[`RunAsync`](src/Nullean.Argh/Runtime/ArghRuntime.cs) dispatches into **generated** code in your assembly.

### Hosting package (`Nullean.Argh.Hosting`)

Use when the app is already built on **`Microsoft.Extensions.Hosting`** and you want commands and middleware registered in DI with lifetimes, `CancellationToken` linked to the host, etc.

```csharp
using Microsoft.Extensions.Hosting;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArgh(args, b =>
{
    b.Add("hello", MyHandlers.SayHello);
    // b.Add<MyCommandHandlers>(); b.GlobalOptions<MyGlobals>(); …
});

await builder.Build().RunAsync();
```

See [`AddArgh`](src/Nullean.Argh.Hosting/ArghHostingExtensions.cs) for exit behavior and hosted-service ordering.

## Registration model

| API | Purpose |
|-----|---------|
| `Add(name, handler)` | Bind a **command name** to a **delegate**. Prefer **method groups** (`MethodName`) so the generator can emit direct calls; **lambdas** are supported via runtime storage with a different codegen path. |
| `Add<T>()` | Register **every public method** on `T` as a command (typically used on a static class of handlers). |
| `AddRootCommand(handler)` | Default handler when **no subcommand** is given at the app root (e.g. `app` with only global flags). |
| `AddNamespaceRootCommand(handler)` | Default handler when a **namespace** is selected but no deeper command is (e.g. `app group`). |

Flat apps route `app <command> …`; hierarchical apps route `app <namespace> … <command> …`. The generator emits the switch/dispatch tree accordingly.

## Namespaces

`AddNamespace(name, description, configure)` and `AddNamespace<T>(name, configure)` create a **segment** in the command path (similar in spirit to ASP.NET’s `MapGroup`). `AddNamespace<T>` can auto-register public methods from `T` and nested handler types—**do not** call `Add<T>()` again for the same type inside the callback.

Child namespaces can nest. The generator produces separate help printers for namespace overview and leaf commands.

## Global and namespace options

- **`GlobalOptions<T>()`** — flags parsed **before** routing; available everywhere (subject to help/layout rules).
- **`CommandNamespaceOptions<T>()`** — options scoped to a namespace. **`T` must inherit the parent namespace’s options type** (or the global options type at the root); the generator reports an error if the chain is wrong.

Parsing order is reflected in generated code: globals first, then namespace option types along the path, then command flags and positionals.

## Parameters and binding

**Defaults**

- Parameters without `[Argument]` are **long** flags: `--kebab-case` from the parameter name, optional short names via XML param docs (`-c`, aliases) where configured.
- `[Argument]` marks **positional** arguments; indices must start at `0` and be consecutive.

**Common types**

- Primitives, `string`, `bool` / `bool?` (including `--flag` / `--no-flag` style where applicable).
- `enum`, `FileInfo`, `DirectoryInfo`, `Uri`.
- Collections (`List<T>`, etc.): repeated flags or `[CollectionSyntax(Separator = "...")]` for a single split value.

**DTOs**

- `[AsParameters]` on a parameter expands **public** properties/fields (and primary-constructor parameters) into flags or `[Argument]` positionals. Optional `[AsParameters("prefix")]` adds a long-name prefix (e.g. `--app-name`).

**Custom parsing**

- `[ArgumentParser(typeof(MyParser))]` with [`IArgumentParser<T>`](src/Nullean.Argh/Parsing/IArgumentParser.cs).

## Help and XML documentation

- **Global**: `--help` / `-h`, `--version` at the root; per-command **`-h`/`--help`** prints that command’s help.
- Styling: ANSI colors when appropriate; **`NO_COLOR`** disables decoration (see [`CliHelpFormatting`](src/Nullean.Argh/Help/CliHelpFormatting.cs)).
- Enable **`GenerateDocumentationFile`** on your project so handler XML is available; the generator emits help text that includes **summary**, **arguments/options**, **remarks**, and **`<example>`** content where present.
- In **remarks** only, the generator can rewrite XML for CLI context: **`paramref`** to a flag becomes `--name`; **`see cref`** to another handler can become that command’s **usage synopsis** (same tail as the `Usage:` line). See the example project.

Help output is **generated** as C# strings and `WriteLine` calls—your `.xml` doc file is not read at runtime.

## Middleware

- **Global**: `UseMiddleware<T>()` or inline `UseMiddleware(async (ctx, next) => …)`.
- **Per-command**: `[MiddlewareAttribute<TMiddleware>]` on a handler method.

[`ICommandMiddleware`](src/Nullean.Argh/Middleware/CommandMiddleware.cs) receives [`CommandContext`](src/Nullean.Argh/Middleware/CommandMiddleware.cs) (`CommandPath`, `Args`, `ExitCode`, `CancellationToken`, …). Middleware **does not run** for root `--help`, `--version`, `--completions`, or when printing command help before the handler runs.

The pipeline is **wired in generated code** (not reflection over a list of delegates at runtime).

## Dependency injection

[`ArghServices.ServiceProvider`](src/Nullean.Argh/Runtime/ArghHostRuntime.cs) is set when running under a host. For **`Add<T>()`** instance methods and **`UseMiddleware<T>()`** / `[MiddlewareAttribute<T>]`, generated code uses **`GetService(typeof(T))`** when a provider is present; otherwise **`new T()`** (parameterless constructors).

For native AOT / trimming, register handler and middleware types in DI so required constructors are preserved.

## Hosting

`services.AddArgh(args, b => { … })` ([`AddArgh`](src/Nullean.Argh.Hosting/ArghHostingExtensions.cs)):

- Registers your fluent `ArghApp` configuration **via** [`ArghHostingBuilder`](src/Nullean.Argh.Hosting/ArghHostingBuilder.cs) (mirrors `Add`/`Add<T>`/`GlobalOptions`/`UseMiddleware`/`AddNamespace`, and can register types with lifetimes).
- Adds a hosted service that runs **`ArghRuntime.RunAsync(args)`** and then **`Environment.Exit`** with the exit code so normal host output does not continue after the CLI finishes.
- Links **`CancellationToken`** on command handlers to console cancellation and **`IHostApplicationLifetime.ApplicationStopping`**.

**Register `AddArgh` before other `IHostedService` registrations** if you want the CLI (including `--help`) to run first and exit without starting later background work. Services registered *before* `AddArgh` still get `StartAsync` on every invocation.

## Routing API

[`ArghParser.Route(args)`](src/Nullean.Argh/Runtime/ArghParser.cs) returns a [`RouteMatch`](src/Nullean.Argh/Runtime/ArghParser.cs) (`CommandPath`, `RemainingArgs`) without invoking handlers—useful for tests and tooling.

## User experience

Unknown commands (typos) can trigger **fuzzy suggestions** (limited edit distance) in generated error paths so users see likely command names.

## Shell completions

At the root, **`--completions bash|zsh|fish`** prints a **shell script template** from [`CompletionScriptTemplates`](src/Nullean.Argh/Help/CompletionScriptTemplates.cs). Templates use `{0}` placeholders for the executable name and assume a **`__complete`** protocol (e.g. `myapp __complete bash -- …`). **You must implement** that protocol (or adapt the script) if you want live completions; the templates are **not** a full completion engine by themselves.

## License and links

- **License**: [MIT](LICENSE)
- **Repository**: [github.com/nullean/argh](https://github.com/nullean/argh)
- **Releases**: [GitHub releases](https://github.com/nullean/argh/releases)

This README is the same file used as the **NuGet package readme** for `Nullean.Argh` and `Nullean.Argh.Hosting`.
