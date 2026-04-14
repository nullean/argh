# Nullean.Argh

Build full-featured .NET CLIs without writing a parser.

Register commands on [`ArghApp`](src/Nullean.Argh.Core/ArghApp.cs). A Roslyn source generator emits parsing, routing, dispatch, and help into your assembly at build time — no reflection, no runtime overhead, trimming- and AOT-safe by default.

*Inspired by [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) (Cysharp) — same source-generated direction, different API and packaging.*

![Sample CLI help output (XmlDocShowcase)](https://cdn.jsdelivr.net/gh/nullean/argh@main/docs/assets/xml-doc-showcase-help.gif)

**Table of contents**

- [Features](#features)
- [Packages](#packages)
- [Choosing a package](#choosing-a-package)
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

## Features

- **Your XML doc is your help text** — enable `GenerateDocumentationFile` and summaries, remarks, param descriptions, and `<example>` blocks appear in `--help` output automatically. No separate attribute layer, no string duplication.
- **Everything is generated C#** — the source generator emits a typed dispatch tree, option parsers, and help printers directly into your assembly. Read the generated code, step through it in a debugger, ship it trimmed or AOT-compiled.
- **Grouped commands that feel like `MapGroup`** — nested namespaces with their own help pages, scoped option types, and separate listings. If you've used ASP.NET minimal APIs, the registration model is immediately familiar.
- **`[AsParameters]` binding** — records and classes expand into flags or positionals without a custom bind loop. `[AsParameters("prefix")]` adds a long-name prefix across all members.
- **Host-ready** — [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) plugs the same model into `Microsoft.Extensions.DependencyInjection` and `IHost`: DI lifetimes, `CancellationToken` linked to the host, and `IHostApplicationLifetime` integration.

## Packages

### Choosing a package

**Add a single top-level NuGet package:** use **`Nullean.Argh`** for a console-only CLI, or **`Nullean.Argh.Hosting`** for **`Microsoft.Extensions.*`** and the generic host. Each pulls in **`Nullean.Argh.Core`** (runtime + analyzer) and **`Nullean.Argh.Interfaces`** transitively—you do not reference Core or Interfaces manually for normal apps.

| Package | When to use | Role |
|--------|---------------|------|
| [`Nullean.Argh`](https://www.nuget.org/packages/Nullean.Argh) | Default for **console** apps (one metapackage reference) | References **`Nullean.Argh.Core`** + **`Nullean.Argh.Interfaces`**. |
| [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) | **`IHost`**, DI lifetimes, ME.* | **`AddArgh`** / [`ArghHostingBuilder`](src/Nullean.Argh.Hosting/ArghHostingBuilder.cs). Depends on **Core** + **Interfaces** only—**not** the **`Nullean.Argh`** metapackage. |
| [`Nullean.Argh.Core`](https://www.nuget.org/packages/Nullean.Argh.Core) | Advanced: runtime + analyzer **without** the metapackage | **`ArghApp`**, `ArghRuntime`, help, embedded analyzer; no **`Microsoft.Extensions.*`**. [`ArghServices.ServiceProvider`](src/Nullean.Argh.Core/Runtime/ArghHostRuntime.cs) uses BCL **`System.IServiceProvider`**. |
| [`Nullean.Argh.Interfaces`](https://www.nuget.org/packages/Nullean.Argh.Interfaces) | **Shared libraries** (e.g. reusable middleware) with **minimal** surface | Attributes, `IArghBuilder`, middleware/parser contracts—often pulled transitively; reference alone only when you intentionally avoid Core. |

**`Nullean.Argh.Generator`** is **not** published as its own NuGet package. It is built into the repo and **shipped inside `Nullean.Argh.Core`** under `analyzers/dotnet/cs` when you consume Core or the **`Nullean.Argh`** metapackage. The generator also inspects **metadata reference** display names (similar in spirit to [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework)’s approach) for capability flags; emitted dispatch behavior is unchanged for now.

**Console app (single package reference)**

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh" />
</ItemGroup>
```

**Hosted app (single package reference)**

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Hosting" />
</ItemGroup>
```

**Advanced:** reference **`Nullean.Argh.Core`** directly if you want the runtime and analyzer without the **`Nullean.Argh`** metapackage (**`Nullean.Argh.Interfaces`** comes in transitively).

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Core" />
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

[`RunAsync`](src/Nullean.Argh.Core/Runtime/ArghRuntime.cs) dispatches into **generated** code in your assembly.

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

Commands are method groups or delegates. The generator emits a direct call per registration — no runtime switch over strings.

| API | Purpose |
|-----|---------|
| `Add(name, handler)` | Bind a **command name** to a **delegate**. Prefer **method groups** (`MethodName`) so the generator can emit direct calls; **lambdas** are supported via runtime storage with a different codegen path. |
| `Add<T>()` | Register **every public method** on `T` as a command (typically used on a static class of handlers). |
| `AddRootCommand(handler)` | Default handler when **no subcommand** is given at the app root (e.g. `app` with only global flags). |
| `AddNamespaceRootCommand(handler)` | Default handler when a **namespace** is selected but no deeper command is (e.g. `app group`). |

Flat apps route `app <command> …`; hierarchical apps route `app <namespace> … <command> …`. The generator emits the switch/dispatch tree accordingly.

## Namespaces

Group related commands under a shared path, scoped options, and their own help page — the same mental model as ASP.NET's `MapGroup`.

`AddNamespace(name, description, configure)` and `AddNamespace<T>(name, configure)` create a **segment** in the command path. `AddNamespace<T>` can auto-register public methods from `T` and nested handler types — **do not** call `Add<T>()` again for the same type inside the callback.

Child namespaces can nest. The generator produces separate help printers for namespace overview and leaf commands.

## Global and namespace options

Flags that apply across commands don't belong on every method signature.

- **`GlobalOptions<T>()`** — flags parsed **before** routing; available everywhere (subject to help/layout rules).
- **`CommandNamespaceOptions<T>()`** — options scoped to a namespace. **`T` must inherit the parent namespace's options type** (or the global options type at the root); the generator reports an error if the chain is wrong.

Parsing order is reflected in generated code: globals first, then namespace option types along the path, then command flags and positionals.

## Parameters and binding

Method parameters become CLI flags automatically. No attribute boilerplate for the common case.

**Defaults**

- Parameters without `[Argument]` are **long flags**: `--kebab-case` from the parameter name, optional short names via XML param docs (`-c`, aliases) where configured.
- `[Argument]` marks **positional** arguments; indices must start at `0` and be consecutive.

**Common types**

- Primitives, `string`, `bool` / `bool?` (including `--flag` / `--no-flag` style where applicable).
- `enum`, `FileInfo`, `DirectoryInfo`, `Uri`.
- Collections (`List<T>`, etc.): repeated flags or `[CollectionSyntax(Separator = "...")]` for a single split value.

**DTOs**

- `[AsParameters]` on a parameter expands **public** properties/fields (and primary-constructor parameters) into flags or `[Argument]` positionals. Optional `[AsParameters("prefix")]` adds a long-name prefix (e.g. `--app-name`).

**Custom parsing**

- `[ArgumentParser(typeof(MyParser))]` with [`IArgumentParser<T>`](src/Nullean.Argh.Interfaces/Parsing/IArgumentParser.cs).

## Help and XML documentation

This is the feature that removes a whole category of maintenance work. Write your XML doc once; the generator reads it at build time and bakes the text into generated `--help` output. Your `.xml` doc file is not read at runtime.

- **Global**: `--help` / `-h`, `--version` at the root; per-command **`-h`/`--help`** prints that command's help.
- Styling: ANSI colors when appropriate; **`NO_COLOR`** disables decoration (see [`CliHelpFormatting`](src/Nullean.Argh.Core/Help/CliHelpFormatting.cs)).
- Enable **`GenerateDocumentationFile`** on your project; the generator emits help text from **summary**, **arguments/options**, **remarks**, and **`<example>`** content.
- In **remarks**, the generator rewrites XML for CLI context: **`paramref`** to a flag becomes `--name`; **`see cref`** to another handler becomes that command's **usage synopsis**. See [`examples/XmlDocShowcase`](examples/XmlDocShowcase).

## Middleware

Cross-cutting logic — auth checks, logging, timing — lives in middleware and stays out of handler methods.

- **Global**: `UseMiddleware<T>()` or inline `UseMiddleware(async (ctx, next) => …)`.
- **Per-command**: `[MiddlewareAttribute<TMiddleware>]` on a handler method.

[`ICommandMiddleware`](src/Nullean.Argh.Interfaces/Middleware/CommandMiddleware.cs) receives [`CommandContext`](src/Nullean.Argh.Interfaces/Middleware/CommandMiddleware.cs) (`CommandPath`, `Args`, `ExitCode`, `CancellationToken`, …). Middleware **does not run** for root `--help`, `--version`, `--completions`, or when printing command help before the handler runs.

The pipeline is **wired in generated code** — not a runtime delegate chain.

## Dependency injection

[`ArghServices.ServiceProvider`](src/Nullean.Argh.Core/Runtime/ArghHostRuntime.cs) is typed as **`System.IServiceProvider`** and is set when running under a host (e.g. via [`Nullean.Argh.Hosting`](src/Nullean.Argh.Hosting/ArghHostingExtensions.cs)). For **`Add<T>()`** instance methods and **`UseMiddleware<T>()`** / `[MiddlewareAttribute<T>]`, generated code uses **`GetService(typeof(T))`** when a provider is present; otherwise **`new T()`** (parameterless constructors).

For native AOT / trimming, register handler and middleware types in DI so required constructors are preserved.

## Hosting

**`Nullean.Argh.Hosting`** does not depend on the **`Nullean.Argh`** metapackage; add **`Nullean.Argh.Hosting`** alone for hosted apps.

`services.AddArgh(args, b => { … })` ([`AddArgh`](src/Nullean.Argh.Hosting/ArghHostingExtensions.cs)):

- Registers your fluent `ArghApp` configuration **via** [`ArghHostingBuilder`](src/Nullean.Argh.Hosting/ArghHostingBuilder.cs) (mirrors `Add`/`Add<T>`/`GlobalOptions`/`UseMiddleware`/`AddNamespace`, and can register types with lifetimes).
- Adds a hosted service that runs **`ArghRuntime.RunAsync(args)`** and then **`Environment.Exit`** with the exit code so normal host output does not continue after the CLI finishes.
- Links **`CancellationToken`** on command handlers to console cancellation and **`IHostApplicationLifetime.ApplicationStopping`**.

**Register `AddArgh` before other `IHostedService` registrations** if you want the CLI (including `--help`) to run first and exit without starting later background work. Services registered *before* `AddArgh` still get `StartAsync` on every invocation.

## Routing API

[`ArghParser.Route(args)`](src/Nullean.Argh.Core/Runtime/ArghParser.cs) returns a [`RouteMatch`](src/Nullean.Argh.Core/Runtime/ArghParser.cs) (`CommandPath`, `RemainingArgs`) without invoking handlers — useful for tests and tooling.

## User experience

Typos produce actionable errors, not a wall of usage text.

```
$ myapp dploy --env prod
Error: unknown command 'dploy'. Did you mean 'deploy'?

Run 'myapp deploy --help' for usage.
Run 'myapp --help' for usage.
```

The fuzzy matcher runs at the right scope: a typo inside a namespace suggests the qualified name (`docs remarks`, not just `remarks`) and links to the namespace help page. Unknown commands outside any match print the full command list.

## Shell completions

At the root, **`--completions bash|zsh|fish`** prints a **shell script template** from [`CompletionScriptTemplates`](src/Nullean.Argh.Core/Help/CompletionScriptTemplates.cs). Templates use `{0}` placeholders for the executable name and assume a **`__complete`** protocol (e.g. `myapp __complete bash -- …`). **You must implement** that protocol (or adapt the script) if you want live completions; the templates are **not** a full completion engine by themselves.

## License and links

- **License**: [MIT](LICENSE)
- **Repository**: [github.com/nullean/argh](https://github.com/nullean/argh)
- **Releases**: [GitHub releases](https://github.com/nullean/argh/releases)

This README is packed as the **NuGet package readme** for **`Nullean.Argh`**, **`Nullean.Argh.Core`**, **`Nullean.Argh.Interfaces`**, and **`Nullean.Argh.Hosting`**.
