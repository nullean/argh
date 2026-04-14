# Nullean.Argh

Build full-featured .NET CLIs without writing a parser.

Register commands on [`ArghApp`](src/Nullean.Argh.Core/ArghApp.cs). A Roslyn source generator emits parsing, routing, dispatch, and help into your assembly at build time — no reflection, no runtime overhead, trimming- and AOT-safe by default.

*Inspired by [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) (Cysharp) — same source-generated direction, different API and packaging.*

![Sample CLI help output (XmlDocShowcase)](https://cdn.jsdelivr.net/gh/nullean/argh@main/docs/assets/xml-doc-showcase-help.gif)

**Table of contents**

- [Features](#features)
- [Packages](#packages)
- [Quick start](#quick-start)
- [Registration model](#registration-model)
- [Namespaces](#namespaces)
- [Parameters and binding](#parameters-and-binding)
- [Object binding](#object-binding)
- [Help and XML documentation](#help-and-xml-documentation)
- [Middleware](#middleware)
- [Dependency injection](#dependency-injection)
- [Hosting](#hosting)
- [Routing API](#routing-api)
- [Shell completions](#shell-completions)
- [License and links](#license-and-links)

## Features

- **Your XML doc is your help text** — enable `GenerateDocumentationFile` and summaries, remarks, param descriptions, and `<example>` blocks appear in `--help` output automatically. No separate attribute layer, no string duplication.
- **Everything is generated C#** — the source generator emits a typed dispatch tree, option parsers, and help printers directly into your assembly. Read the generated code, step through it in a debugger, ship it trimmed or AOT-compiled.
- **Grouped commands that feel like `MapGroup`** — nested namespaces with their own help pages, scoped option types, and separate listings. If you've used ASP.NET minimal APIs, the registration model is immediately familiar.
- **`[AsParameters]` binding** — records expand into flags or positionals without a custom bind loop. `[AsParameters("prefix")]` adds a long-name prefix across all members.
- **Host-ready** — [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) plugs the same model into `Microsoft.Extensions.DependencyInjection` and `IHost`: DI lifetimes, `CancellationToken` linked to the host, and `IHostApplicationLifetime` integration.

## Packages

**Add a single top-level NuGet package:** use **`Nullean.Argh`** for a console-only CLI, or **`Nullean.Argh.Hosting`** for `Microsoft.Extensions.*` and the generic host. Each pulls in **`Nullean.Argh.Core`** (runtime + analyzer) and **`Nullean.Argh.Interfaces`** transitively — you do not reference Core or Interfaces manually for normal apps.

| Package | When to use | Role |
|--------|-------------|------|
| [`Nullean.Argh`](https://www.nuget.org/packages/Nullean.Argh) | Default for **console** apps | Metapackage — references **Core** + **Interfaces**. No `Microsoft.Extensions.*` dependency. |
| [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) | `IHost`, DI lifetimes, ME.* | `AddArgh` / [`ArghHostingBuilder`](src/Nullean.Argh.Hosting/ArghHostingBuilder.cs). Depends on **Core** + **Interfaces** only — not the metapackage. |
| [`Nullean.Argh.Core`](https://www.nuget.org/packages/Nullean.Argh.Core) | Advanced: runtime + analyzer without the metapackage | `ArghApp`, `ArghRuntime`, help, embedded analyzer. No `Microsoft.Extensions.*`. |
| [`Nullean.Argh.Interfaces`](https://www.nuget.org/packages/Nullean.Argh.Interfaces) | Shared libraries (e.g. reusable middleware or parsers) | Attributes, `IArghBuilder`, middleware/parser contracts. Zero external dependencies. Reference directly only when you intentionally avoid Core. |

**`Nullean.Argh.Generator`** is not a separate NuGet package — it ships embedded inside `Nullean.Argh.Core` under `analyzers/dotnet/cs`.

**Console app**

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh" />
</ItemGroup>
```

**Hosted app**

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Hosting" />
</ItemGroup>
```

**Shared middleware / parser library**

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Interfaces" />
</ItemGroup>
```

## Quick start

### Console app (`Nullean.Argh`)

```csharp
using Nullean.Argh;

var app = new ArghApp();
app.Add("hello", MyHandlers.SayHello);

return await app.RunAsync(args);
```

[`RunAsync`](src/Nullean.Argh.Core/Runtime/ArghRuntime.cs) dispatches into generated code in your assembly.

### Hosted app (`Nullean.Argh.Hosting`)

Use when the app is already built on `Microsoft.Extensions.Hosting` and you want commands and middleware registered in DI with lifetimes, `CancellationToken` linked to the host, etc.

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

Three forms, same registration surface:

```csharp
// 1. Method group — preferred. Generator emits a direct typed call.
app.Add("deploy", DeployHandlers.Run);

// 2. Lambda — convenient for simple one-liners.
app.Add("greet", (string name) => Console.WriteLine($"Hello, {name}!"));

// 3. Class — registers every public method on T as a command.
app.Add<StorageHandlers>();
```

| API | Purpose |
|-----|---------|
| `Add(name, handler)` | Bind a command name to a delegate. Prefer method groups; lambdas are supported via runtime storage with a different codegen path. |
| `Add<T>()` | Register every public method on `T` as a command (typically a static class of handlers). |
| `AddRootCommand(handler)` | Default handler when no subcommand is given at the app root. |
| `AddNamespaceRootCommand(handler)` | Default handler when a namespace is selected but no deeper command is. |

Flat apps route `app <command> …`; hierarchical apps route `app <namespace> … <command> …`. The generator emits the switch/dispatch tree accordingly.

## Namespaces

Group related commands under a shared path, scoped options, and their own help page — the same mental model as ASP.NET's `MapGroup`.

```csharp
app.AddNamespace<StorageCommands>("storage", ns =>
{
    ns.AddNamespace<BlobCommands>("blob", blobs =>
    {
        blobs.Add("upload", BlobHandlers.Upload);
        blobs.Add("download", BlobHandlers.Download);
    });
    ns.Add("list", StorageHandlers.List);
});
// Resulting paths:
//   storage list
//   storage blob upload
//   storage blob download
```

`AddNamespace<T>` auto-registers public methods from `T` and nested handler types — do not call `Add<T>()` again for the same type inside the callback. The generator produces separate help printers for namespace overview and leaf commands.

## Parameters and binding

Method parameters become CLI flags automatically. No attribute boilerplate for the common case.

### Arguments (positional)

Mark a parameter with `[Argument]` to make it positional. Indices must start at `0` and be consecutive.

```csharp
public static Task<int> Deploy([Argument] string environment) { … }
// myapp deploy production
```

### Flags (named options)

Parameters without `[Argument]` become `--kebab-case` long flags. A `bool` flag defaults to `false`; pass `--flag` to set it.

```csharp
public static Task<int> Build(string outputDir, bool release = false) { … }
// myapp build --output-dir ./bin --release
```

### Supported types

| Category | Types |
|----------|-------|
| Primitives | `string`, `int`, `long`, `double`, `float`, `decimal`, `bool`, `bool?` |
| System | `enum`, `FileInfo`, `DirectoryInfo`, `Uri` |
| Collections | `List<T>`, `T[]` — repeated flag or `[CollectionSyntax(Separator=",")]` for a single split value |

### `[AsParameters]` — DTO expansion

A record or class parameter annotated with `[AsParameters]` expands its public properties into individual flags or positionals. Add a string to prefix all long names.

```csharp
public record DeployOptions(string Environment, bool DryRun = false);

public static Task<int> Deploy([AsParameters] DeployOptions opts) { … }
// myapp deploy --environment staging --dry-run

public record AppOptions(string Name, string Version = "");
public static Task<int> Configure([AsParameters("app")] AppOptions opts) { … }
// myapp configure --app-name foo --app-version 2
```

### Custom parsing — `IArgumentParser<T>`

For types with no built-in support, implement `IArgumentParser<T>` and annotate the parameter:

```csharp
public class SemVerParser : IArgumentParser<SemVer>
{
    public static bool TryParse(string value, out SemVer result) =>
        SemVer.TryParse(value, out result);
}

public static Task<int> Release([ArgumentParser(typeof(SemVerParser))] SemVer version) { … }
// myapp release 1.2.3
```

`IArgumentParser<T>` is in [`Nullean.Argh.Interfaces`](src/Nullean.Argh.Interfaces/Parsing/IArgumentParser.cs).

## Object binding

Share state across commands without repeating parameters on every method signature.

### Global options

```csharp
public record GlobalOptions(bool Verbose = false);

app.GlobalOptions<GlobalOptions>();
app.Add("build", (GlobalOptions g) => { if (g.Verbose) … });
// myapp build --verbose
```

Globals are parsed before routing and available to every command.

### Namespace options

Scoped to a namespace and its children. The options type must inherit the parent's options type — `GlobalOptions` at the root, or the enclosing namespace's options further down. The generator reports an error (AGH0004) if the chain is broken.

```csharp
public record StorageOptions(string ConnectionString = "") : GlobalOptions;

app.AddNamespace<StorageHandlers>("storage", ns =>
{
    ns.CommandNamespaceOptions<StorageOptions>();
    ns.Add("list", (StorageOptions o) => { … });
});
// myapp storage list --connection-string "…" --verbose
```

Parsing order in generated code: globals → namespace options along the path → command flags and positionals.

> **Note:** commands are not currently required to declare the namespace options type as a parameter — the injection is optional.

## Help and XML documentation

This is the feature that removes a whole category of maintenance work. Write XML doc once; the generator reads it at build time and bakes the text into `--help` output. Your `.xml` doc file is not read at runtime.

Enable `GenerateDocumentationFile` in your project file, then document your handlers normally:

```csharp
/// <summary>Deploy the application to the target environment.</summary>
/// <remarks>
/// Runs pre-flight checks before deploying. Pass <c>--dry-run</c> to
/// validate without making changes. See also <see cref="Rollback"/>.
/// </remarks>
/// <param name="environment">Target environment (staging, production).</param>
/// <param name="dryRun">Validate only — make no changes.</param>
public static Task<int> Deploy(string environment, bool dryRun = false) { … }
```

The generated `myapp deploy --help` output:

```
Usage: myapp deploy <environment> [options]

   Deploy the application to the target environment.

Global options:
  --help, -h              Show help.

Arguments:
  <environment>           Target environment (staging, production).

Options:
  --dry-run               Validate only — make no changes.

Notes:
  Runs pre-flight checks before deploying. Pass --dry-run to validate
  without making changes. See also: myapp rollback <args>
```

In **remarks**, `<paramref>` to a flag becomes `--name`; `<see cref>` to another handler becomes that command's usage synopsis. See [`examples/XmlDocShowcase`](examples/XmlDocShowcase) for the full tag inventory.

**Fuzzy matching.** Typos produce actionable errors with the correct qualified path:

```
$ myapp stoarge list
Error: unknown command or namespace 'stoarge'. Did you mean 'storage'?

Run 'myapp storage --help' for usage.
Run 'myapp --help' for usage.
```

Inside a namespace, the suggestion includes the full path (`storage blob upload`, not just `upload`).

## Middleware

Cross-cutting logic — auth checks, logging, timing — lives in middleware and stays out of handler methods.

```csharp
public class TimingMiddleware : ICommandMiddleware
{
    public async Task InvokeAsync(CommandContext ctx, Func<Task> next)
    {
        var sw = Stopwatch.StartNew();
        await next();
        Console.Error.WriteLine($"{ctx.CommandPath}: {sw.ElapsedMilliseconds}ms");
    }
}

// Global — runs for every command
app.UseMiddleware<TimingMiddleware>();

// Per-handler — attribute on the method
[MiddlewareAttribute<TimingMiddleware>]
public static Task<int> Deploy(string environment) { … }
```

[`ICommandMiddleware`](src/Nullean.Argh.Interfaces/Middleware/CommandMiddleware.cs) receives [`CommandContext`](src/Nullean.Argh.Interfaces/Middleware/CommandMiddleware.cs) with `CommandPath`, `Args`, `ExitCode`, and `CancellationToken`. Middleware does not run for `--help`, `--version`, or `--completions`. The pipeline is wired in generated code — not a runtime delegate chain.

## Dependency injection

[`ArghServices.ServiceProvider`](src/Nullean.Argh.Core/Runtime/ArghHostRuntime.cs) is typed as `System.IServiceProvider` and set when running under a host. For `Add<T>()` instance methods and `UseMiddleware<T>()` / `[MiddlewareAttribute<T>]`, generated code resolves via `GetService(typeof(T))` when a provider is present; otherwise it falls back to `new T()`.

```csharp
// Handler with an injected service
public class DeployCommands(IDeployService deployer)
{
    public async Task<int> Run(string environment)
    {
        await deployer.DeployAsync(environment);
        return 0;
    }
}

// Registration — service must be in the DI container
builder.Services.AddScoped<IDeployService, DeployService>();
builder.Services.AddArgh(args, b => b.Add<DeployCommands>());
```

For native AOT / trimming, register handler and middleware types explicitly in DI so required constructors are preserved.

## Hosting

`Nullean.Argh.Hosting` does not depend on the `Nullean.Argh` metapackage — add it alone for hosted apps.

`services.AddArgh(args, b => { … })` ([`AddArgh`](src/Nullean.Argh.Hosting/ArghHostingExtensions.cs)):

- Registers your `ArghApp` configuration via [`ArghHostingBuilder`](src/Nullean.Argh.Hosting/ArghHostingBuilder.cs) — mirrors `Add`/`Add<T>`/`GlobalOptions`/`UseMiddleware`/`AddNamespace` and can register types with DI lifetimes.
- Adds a hosted service that runs `ArghRuntime.RunAsync(args)` then `Environment.Exit` with the exit code so host output does not continue after the CLI finishes.
- Links `CancellationToken` on command handlers to console cancellation and `IHostApplicationLifetime.ApplicationStopping`.

**Register `AddArgh` before other `IHostedService` registrations** if you want the CLI (including `--help`) to run first and exit without starting later background work. Services registered *before* `AddArgh` still get `StartAsync` on every invocation.

## Routing API

[`ArghParser.Route(args)`](src/Nullean.Argh.Core/Runtime/ArghParser.cs) returns a [`RouteMatch`](src/Nullean.Argh.Core/Runtime/ArghParser.cs) (`CommandPath`, `RemainingArgs`) without invoking handlers — useful for tests and tooling.

## Shell completions

`--completions bash|zsh|fish` prints a shell integration script from [`CompletionScriptTemplates`](src/Nullean.Argh.Core/Help/CompletionScriptTemplates.cs) with the executable name substituted. The scripts wire tab completion to your binary by calling `myapp __complete <shell> -- <partial>`.

A full completion engine (handling `__complete` at runtime and returning matching commands and flags) and `--cli-schema` JSON export (a machine-readable command tree for tooling and LLM integrations) are planned.

## License and links

- **License**: [MIT](LICENSE)
- **Repository**: [github.com/nullean/argh](https://github.com/nullean/argh)
- **Releases**: [GitHub releases](https://github.com/nullean/argh/releases)

This README is the NuGet package readme for `Nullean.Argh`, `Nullean.Argh.Core`, `Nullean.Argh.Interfaces`, and `Nullean.Argh.Hosting`.
