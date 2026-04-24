# Nullean.Argh

Build full-featured .NET CLIs without writing a parser.

Methods become commands, XML docs become help text, records become option sets. A Roslyn source generator emits parsing, routing, dispatch, and help into your assembly at build time — no reflection, no runtime overhead, trimming- and AOT-safe by default.

Write vanilla C# and get a fully functional CLI in return: rich `--help` output, shell tab-completions for bash, zsh, and fish, and a machine-readable JSON schema ready for agentic use cases — all without writing a single line of plumbing code for any of it.

***Heavily** Inspired by [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) (Cysharp) — rewritten from scratch with a different feature set, but ConsoleAppFramework laid out the path for source-generated CLI's in .NET.*

![Sample CLI help output (XmlDocShowcase)](https://cdn.jsdelivr.net/gh/nullean/argh@main/docs/assets/xml-doc-showcase-help.gif)

**Table of contents**

- [Features](#features)
- [Packages](#packages)
- [Quick start](#quick-start)
- [Registration model](#registration-model)
- [Namespaces](#namespaces)
- [Parameters and binding](#parameters-and-binding)
- [CancellationToken (Ctrl+C)](#cancellationtoken-ctrlc)
- [Object binding](#object-binding)
- [Fuzzy matching](#fuzzy-matching)
- [Help and XML documentation](#help-and-xml-documentation)
- [Middleware](#middleware)
- [Dependency injection](#dependency-injection)
- [Hosting](#hosting)
- [Routing API](#routing-api)
- [Validation](#validation)
- [Shell completions](#shell-completions)
- [Schema JSON](#schema-json)
- [License and links](#license-and-links)

## Features

- **XML docs are your help text**
  - Summaries, param descriptions, remarks, and `<example>` blocks appear in `--help` automatically
  - No separate attribute layer, no string duplication
- **Everything is generated C#**
  - Typed dispatch tree, option parsers, and help printers emitted directly into your assembly
  - Read it, step through it in a debugger, ship it trimmed or AOT-compiled
- **`MapGroup`-style namespaces**
  - Nested command groups with their own help pages and scoped option types
  - Immediately familiar if you've used ASP.NET minimal APIs
- **DTO binding with `[AsParameters]`**
  - Records and classes expand into flags without a custom bind loop
  - Optional prefix (`[AsParameters("app")]`) namespaces all long names
- **Shell completions built-in**
  - Generated lookup tables for subcommands, namespaces, and flags — no extra package
  - One install command per shell (bash, zsh, fish)
- **Agent-ready schema**
  - `myapp __schema` emits a full JSON description of commands, options, summaries, and examples
  - Feed it to an LLM, a docs generator, or diff it in CI to catch breaking changes
- **Fuzzy matching**
  - Typos produce actionable errors with the correct qualified path and a `--help` suggestion
  - No silent no-match
- **DataAnnotations validation**
  - Annotate parameters and DTO members with `[Range]`, `[StringLength]`, `[RegularExpression]`, `[AllowedValues]`, and more
  - Constraints appear in `--help`; violations print to stderr and exit with code 2 — no reflection, no runtime dependency
- **Cancellation on command handlers**
  - Add `CancellationToken` to a handler signature — it is injected, not a flag; by default it tracks **Ctrl+C** (console cancel)
- **Zero-dep or ME.* native**
  - `Nullean.Argh` — no `Microsoft.Extensions.*` dependency
  - `Nullean.Argh.Hosting` — same registration surface, plugs into `IHost` and DI

## Packages

**Which package do I need?** 

* [`Nullean.Argh`](https://www.nuget.org/packages/Nullean.Argh) dependency free version. 
* [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) isolated implementation of `.Core` that fully integrates with `Microsoft.Extensions.*` ecosystem.

Everything else is pulled in transitively, you do not reference `.Core` or `.Interfaces` manually for normal apps. 
The two packages are isolated implementations and both only depend on `.Core`.

* [`Nullean.Argh.Core`](https://www.nuget.org/packages/Nullean.Argh.Core) Shared runtime pulled in by both user-facing packages. Contains `ArghApp`, runtime, help, and the embedded source generator. Not referenced directly in normal apps.
* [`Nullean.Argh.Interfaces`](https://www.nuget.org/packages/Nullean.Argh.Interfaces) Reference directly only when building a shared library (e.g. reusable middleware or parsers) that other Argh-based apps will consume. Contains attributes, `IArghBuilder`, and middleware/parser contracts. Zero external dependencies.

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
app.Map("hello", MyHandlers.SayHello);

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
    b.Map("hello", MyHandlers.SayHello);
    // b.Map<MyCommandHandlers>(); b.UseGlobalOptions<MyGlobals>(); …
});

await builder.Build().RunAsync();
```

See [`AddArgh`](src/Nullean.Argh.Hosting/ArghHostingExtensions.cs) for exit behavior and hosted-service ordering.

## Registration model

Three forms, same registration surface — all are fully supported. With class and method-group registration, XML doc comments on your handler methods flow directly into `--help` output. Lambdas skip that path.

```csharp
// 1. Method group — direct typed dispatch.
app.Map("deploy", DeployHandlers.Run);

// 2. Lambda — convenient for simple one-liners.
app.Map("greet", (string name) => Console.WriteLine($"Hello, {name}!"));

// 3. Class — registers every public method on T as a command.
app.Map<StorageHandlers>();
```

| API | Purpose |
|-----|---------|
| `Map(name, handler)` | Bind a command name to a delegate. |
| `Map<T>()` | Register every public method on `T` as a command (typically a static class of handlers). |
| `MapRoot(handler)` | Default handler when no subcommand is given (at app root, or inside a `MapNamespace` callback for that namespace). |

Flat apps route `app <command> …`; hierarchical apps route `app <namespace> … <command> …`. The generator emits the switch/dispatch tree accordingly.

## Namespaces

Group related commands under a shared path, scoped options, and their own help page — the same mental model as ASP.NET's `MapGroup`.

The idiomatic pattern is to put commands as methods on the class. Nested sub-groups are registered explicitly — there is no auto-discovery of nested types.

```csharp
/// <summary>Commands under <c>storage</c>.</summary>
internal sealed class StorageCommands
{
    /// <summary>List objects in the bucket.</summary>
    public void List() => Console.WriteLine("storage:list");
}

/// <summary>Commands under <c>storage blob</c>.</summary>
internal sealed class BlobCommands
{
    /// <summary>Upload a file.</summary>
    /// <param name="path">-p,--path, Local file path.</param>
    public void Upload(string path) => Console.WriteLine($"storage:blob:upload:{path}");

    /// <summary>Download a file.</summary>
    /// <param name="key">-k,--key, Object key.</param>
    public void Download(string key) => Console.WriteLine($"storage:blob:download:{key}");
}

app.AddNamespace<StorageCommands>("storage", ns =>
{
    ns.AddNamespace<BlobCommands>("blob");
});
// Resulting paths:
//   storage list
//   storage blob upload --path ./file.txt
//   storage blob download --key backups/db.sql
```

The generator produces separate help printers for the namespace overview and each leaf command. Add `CommandNamespaceOptions<T>()` inside the callback to attach scoped options:

```csharp
app.AddNamespace<StorageCommands>("storage", ns =>
{
    ns.CommandNamespaceOptions<StorageOptions>();
    ns.AddNamespace<BlobCommands>("blob");
});
```

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
| Collections | `List<T>`, `T[]` — repeated flag or `[CollectionSyntax(Separator=",")]` for a single comma-separated value |

Collections accept the flag multiple times, or a single comma-separated value via `[CollectionSyntax]`:

```csharp
public static Task<int> Deploy(string[] targets, [CollectionSyntax(Separator = ",")] string[] tags) { … }
// Repeated:   myapp deploy --targets web --targets api
// Separator:  myapp deploy --targets web,api --tags blue,green
```

### Nullable bool — `--flag` / `--no-flag` pairs

A `bool?` flag generates **both** `--flag` (sets `true`) and `--no-flag` (sets `false`). Omitting either leaves the value `null`, letting you distinguish "not specified" from an explicit false. Help output shows `--flag / --no-flag` for nullable bools.

```csharp
public static Task<int> Deploy(string env, bool? dryRun = null) { … }
// myapp deploy staging               → dryRun is null
// myapp deploy staging --dry-run     → dryRun is true
// myapp deploy staging --no-dry-run  → dryRun is false
```

### DTO binding — `[AsParameters]`

A record or class parameter annotated with `[AsParameters]` expands its members into individual flags or positionals. Works with **records** (constructor parameters) and **classes** (public settable properties). Add a string argument to prefix all long names.

```csharp
// Record — constructor parameters become flags
public record DeployOptions(string Environment, bool DryRun = false);

public static Task<int> Deploy([AsParameters] DeployOptions opts) { … }
// myapp deploy --environment staging --dry-run

// Class — public settable properties become flags
public class BuildOptions
{
    public string OutputDir { get; set; } = "";
    public bool Release { get; set; }
}

public static Task<int> Build([AsParameters] BuildOptions opts) { … }
// myapp build --output-dir ./bin --release

// Prefix — all long names get a common prefix
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

### CancellationToken (Ctrl+C)

Add `System.Threading.CancellationToken` as a **parameter of the command handler method** (alongside flags and positionals). It is **not** parsed from the command line and does not appear in `--help` — the source generator **injects** the token the runtime uses for cooperative cancellation.

You can also add it on an **`[AsParameters]`** type as a **primary constructor parameter** or **`init` property** (same injection rules). Keep **CLI-bound members first** in declaration order: all `[Argument]` positionals must precede flags, and **`CancellationToken` must not appear between a flag and a later positional** (the usual pattern is to put the token **last** on the DTO).

- **Console and `ArghApp`:** the token is cancelled when the user presses **Ctrl+C** (and on Windows, the console **break** signal). The process keeps running after cancel unless your handler exits; Argh only forwards cancellation to your code.
- **`Nullean.Argh.Hosting` / `AddArgh`:** the same console token is **linked** with `IHostApplicationLifetime.ApplicationStopping`, so the parameter also cancels when the host is shutting down.
- **`TryParseArgh` / generated `TryParseDto_*`:** injected `CancellationToken` members are set to **`default`** — there is no host or console token in that API, so the value is non-cancellable.

```csharp
public static async Task<int> Sync(
    string source,
    CancellationToken cancellationToken)
{
    await CopyTreeAsync(source, cancellationToken);
    return 0;
}
// myapp sync --source ./data   (CancellationToken is not a CLI option)

public record RunArgs(string Source, int Port, CancellationToken Ct);

public static async Task<int> Run([AsParameters] RunArgs args)
{
    await Task.Delay(1, args.Ct);
    return 0;
}
```

## Object binding

Share state across commands without repeating parameters on every method signature.

### Global options

```csharp
public record GlobalOptions(bool Verbose = false);

app.UseGlobalOptions<GlobalOptions>();
app.Map("build", (GlobalOptions g) => { if (g.Verbose) … });
// myapp build --verbose
```

Globals are parsed before routing and available to every command.

### Namespace options

Scoped to a namespace and its children. The options type must inherit the parent's options type — `GlobalOptions` at the root, or the enclosing namespace's options further down. The generator reports an error (AGH0004) if the chain is broken.

```csharp
public record StorageOptions(string ConnectionString = "") : GlobalOptions;

app.MapNamespace<StorageHandlers>("storage", ns =>
{
    ns.UseNamespaceOptions<StorageOptions>();
    ns.Map("list", (StorageOptions o) => { … });
});
// myapp storage list --connection-string "…" --verbose
```

Parsing order in generated code: globals → namespace options along the path → command flags and positionals.

### Combining with `[AsParameters]`

A command can extend a global or namespace options type and annotate it with `[AsParameters]` to inherit those flags alongside its own:

```csharp
public record DeployOptions(string Environment, bool DryRun = false) : StorageOptions;

ns.Map("deploy", ([AsParameters] DeployOptions opts) => { … });
// myapp storage deploy --connection-string "…" --environment staging --dry-run
```

> **Note:** commands under a namespace are required to declare the namespace options type as a parameter (enforced by analyzer AGH0021). Annotate the method with `[NoOptionsInjection]` to opt out.

## Fuzzy matching

Typos produce actionable errors with the correct qualified path and a `--help` suggestion:

```
$ myapp stoarge list
Error: unknown command or namespace 'stoarge'. Did you mean 'storage'?

Run 'myapp storage --help' for usage.
Run 'myapp --help' for usage.
```

Inside a namespace, the suggestion includes the full path (`storage blob upload`, not just `upload`).

## Help and XML documentation

Write XML doc once; the generator reads it at build time and bakes the text into `--help` output. No `.xml` doc file is read at runtime — the generator accesses doc comments through the Roslyn compilation model, so **`GenerateDocumentationFile` is not required** for the usual developer inner loop or for routing/parsing/dispatch codegen.

### Test projects referencing Argh apps

If a **test** assembly uses **`InternalsVisibleTo`** to see **`internal`** members of a referenced CLI project, older stacks sometimes hit **CS0436** because the same generated root type name could appear in more than one compilation. The generator emits a **stable, per-assembly** generated root type name so those collisions should not occur. If you must strip analyzers from a specific project (rare), use **`ExcludeAssets`** on the analyzer package reference or an MSBuild target that removes **`Nullean.Argh.Generator`** analyzers from that project only.

### Commands

Document handler methods normally:

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

### Namespaces

Put the `<summary>` (and optionally `<remarks>`) on the class `T` passed to `MapNamespace<T>`. The generator uses it as the namespace description in `myapp storage --help` and in the root command listing:

```csharp
/// <summary>Manage blob and file storage resources.</summary>
/// <remarks>
/// Requires a storage connection string via <c>--connection-string</c>
/// or the <c>STORAGE_CONN</c> environment variable.
/// </remarks>
internal sealed class StorageCommands { … }

app.MapNamespace<StorageCommands>("storage", ns => { … });
```

### Root app

The root `myapp --help` shows a description in two ways:

**`UseCliDescription`** — for apps with no default root command, set a plain one-liner shown beneath the `Usage:` line:

```csharp
app.UseCliDescription("Manage and deploy your application's cloud resources.");
app.MapNamespace<StorageCommands>("storage", ns => { … });
```

`UseCliDescription` is on `IArghRootBuilder` (not `IArghBuilder`), so it is intentionally unavailable inside `MapNamespace` configure callbacks. It cannot be combined with `MapRoot`; the generator reports `AGH0023` if both are present.

**`MapRoot`** — when you also want a default handler at the root, put the XML doc on that handler method. The summary and remarks become the app-level overview:

```csharp
/// <summary>Manage and deploy your application's cloud resources.</summary>
/// <remarks>
/// Run <c>myapp &lt;command&gt; --help</c> for details on any command.
/// </remarks>
public static Task<int> Root() { … }

app.MapRoot(Root);
```

In **remarks**, `<paramref>` to a flag becomes `--name`; `<see cref>` to another handler becomes that command's usage synopsis. See [`examples/XmlDocShowcase`](examples/XmlDocShowcase) for the full tag inventory.

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

[`ICommandMiddleware`](src/Nullean.Argh.Interfaces/Middleware/CommandMiddleware.cs) receives [`CommandContext`](src/Nullean.Argh.Interfaces/Middleware/CommandMiddleware.cs) with `CommandPath`, `Args`, `ExitCode`, and `CancellationToken`. Middleware does not run for `--help`, `--version`, `__completion`, `__complete`, or `__schema`. The pipeline is wired in generated code — not a runtime delegate chain. Each middleware call is emitted as a direct invocation in the generated dispatch method; there is no runtime list to build or iterate.

## Dependency injection

When using `Nullean.Argh.Hosting`, DI integration is fully transparent — register your handler and middleware types in the service collection and the generated code resolves them automatically. No manual `ServiceProvider` wiring needed.

For advanced use or when not using `Nullean.Argh.Hosting`: [`ArghServices.ServiceProvider`](src/Nullean.Argh.Core/Runtime/ArghHostRuntime.cs) is typed as `System.IServiceProvider` and set when running under a host. For `Map<T>()` instance methods and `UseMiddleware<T>()` / `[MiddlewareAttribute<T>]`, generated code resolves via `GetService(typeof(T))` when a provider is present; otherwise it falls back to `new T()`.

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
builder.Services.AddArgh(args, b => b.Map<DeployCommands>());
```

For native AOT / trimming, register handler and middleware types explicitly in DI so required constructors are preserved.

## Hosting

`Nullean.Argh.Hosting` plugs the same command registration model into `IHost` and `Microsoft.Extensions.DependencyInjection` — no custom bootstrapping or glue code needed.

`services.AddArgh(args, b => { … })` ([`AddArgh`](src/Nullean.Argh.Hosting/ArghHostingExtensions.cs)) mirrors the same `Map` / `Map<T>` / `UseGlobalOptions` / `UseNamespaceOptions` / `UseMiddleware` / `MapNamespace` surface as `ArghApp`, and additionally lets you control DI lifetimes:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddArgh(args, b =>
{
    b.MapScoped<DeployCommands>();       // resolved per command invocation
    b.UseMiddleware<AuditMiddleware>(ServiceLifetime.Singleton);   // single instance for the process
    b.Map("ping", PingHandlers.Run);    // static method — no DI lifetime needed
    b.UseGlobalOptions<GlobalOptions>();
});
```

| `IArghHostingBuilder` API | Purpose |
|--------------------------|---------|
| `Map<T>()` | Register `T` as transient and add all its public methods as commands. |
| `MapTransient<T>()` / `MapScoped<T>()` / `MapSingleton<T>()` | Same, with an explicit DI lifetime. |
| `UseGlobalOptions<T>()` | Register `T` as the global options type and add it to DI. |
| `UseMiddleware<TMiddleware>()` | Register middleware as transient. |
| `UseMiddleware<TMiddleware>(lifetime)` | Register middleware with an explicit DI lifetime. |

`AddArgh` registers a hosted service that runs `ArghRuntime.RunAsync(args)` and then calls `Environment.Exit` with the exit code — the host does not continue after the CLI completes.

`CancellationToken` on command handlers: see [CancellationToken (Ctrl+C)](#cancellationtoken-ctrlc) — with hosting, the injected token is linked to **Ctrl+C** and **`IHostApplicationLifetime.ApplicationStopping`**.

**Register `AddArgh` before other `IHostedService` registrations** if you want the CLI (including `--help`) to run first and exit without starting later background work. Services registered *before* `AddArgh` still get `StartAsync` on every invocation.

## Routing API

[`ArghParser.Route(args)`](src/Nullean.Argh.Core/Runtime/ArghParser.cs) returns a [`RouteMatch`](src/Nullean.Argh.Core/Runtime/ArghParser.cs) (`CommandPath`, `RemainingArgs`) without invoking handlers — useful for tests and tooling.

## Validation

Annotate parameters (or `[AsParameters]` members) with standard `System.ComponentModel.DataAnnotations` attributes. The source generator reads the attributes at build time and emits inline validation checks — no reflection, no `Validator.ValidateObject` call, AOT-safe. Constraint hints appear in `--help` after the description; failures print to stderr and exit 2.

```csharp
public static void Deploy(
    [Range(1, 65535)]                          int port,
    [StringLength(64, MinimumLength = 2)]      string name,
    [AllowedValues("dev", "staging", "prod")]  string env,
    [RegularExpression(@"^[a-z0-9\-]+$")]      string slug,
    [UriScheme("https")]                       Uri endpoint)
{ … }
```

```
$ myapp deploy --port 99999
Error: --port: value must be between 1 and 65535.
Run 'myapp deploy --help' for usage.

$ myapp deploy --help
Options:
  --port <int>       [required] [range: 1–65535]
  --name <string>    [required] [length: 2–64]
  --env <string>     [required] [allowed: dev|staging|prod]
  --slug <string>    [required] [pattern: ^[a-z0-9\-]+$]
  --endpoint <uri>   [required] [schemes: https]
```

Supported attributes:

| Attribute | Validates | Help token |
|-----------|-----------|-----------|
| `[Range(min, max)]` | numeric value is within bounds | `[range: min–max]` |
| `[StringLength(max)]` / `[StringLength(max, MinimumLength = min)]` | string length | `[max-length: n]` / `[length: min–max]` |
| `[MinLength(n)]` / `[MaxLength(n)]` | string length (alternative spelling) | `[min-length: n]` / `[max-length: n]` |
| `[Length(min, max)]` (.NET 8) | string length range | `[length: min–max]` |
| `[RegularExpression(pattern)]` | value matches regex | `[pattern: …]` |
| `[AllowedValues(v1, v2, …)]` (.NET 8) | value is in the set | `[allowed: v1\|v2\|…]` |
| `[DeniedValues(v1, v2, …)]` (.NET 8) | value is not in the set | `[denied: v1\|v2\|…]` |
| `[EmailAddress]` | basic `user@host` shape | `[email]` |
| `[Url]` on `string` | absolute URL (http/https/ftp) | `[url]` |
| `[Url]` on `Uri` | scheme is http or https | `[schemes: http\|https]` |
| `[FileExtensions(Extensions="json,yaml")]` | `FileInfo` extension | `[extensions: json\|yaml]` |
| `[UriScheme("https")]` *(Argh-native)* | `Uri` scheme is in the list | `[schemes: https]` |

Enum parameters automatically show `[allowed: Member1\|Member2]` in help — the enum type itself enforces the constraint, no extra attribute needed.

Validation also runs through the `TryParseArgh` static extension emitted for `[AsParameters]` DTOs, so unit tests can assert constraints without spawning a subprocess.

## Shell completions

Tab completion for subcommands, namespaces, and flags is included out of the box: the source generator emits **lookup tables** at compile time (same model as routing and `--help`), and a small `__complete` handler answers the shell with one candidate per line. **`--completions` is not reserved** — use `__completion` / `__complete` only for Argh's integration.

| Command | Purpose |
|--------|---------|
| `myapp __completion bash\|zsh\|fish` | Print an install snippet from [`CompletionScriptTemplates`](src/Nullean.Argh.Core/Help/CompletionScriptTemplates.cs) (substitutes your executable name). |
| `myapp __complete <shell> -- <words...>` | Return completion candidates; `words` are argv after the program name (full line context for nested commands). |

**Bash** — `eval "$(myapp __completion bash)"` (add to `~/.bashrc` to persist).

**Zsh** — `source <(myapp __completion zsh)` (add to `~/.zshrc` to persist).

**Fish** (3.4+ for `commandline -opc`):

```fish
mkdir -p ~/.config/fish/completions
myapp __completion fish > ~/.config/fish/completions/myapp.fish
```

Details: [`CompletionProtocol`](src/Nullean.Argh.Core/Help/CompletionProtocol.cs).

## Schema JSON

`myapp __schema` writes a JSON document to stdout describing your entire CLI — commands, namespaces, global and namespace options, summaries, remarks, usage, and examples. The output is generated at build time from the same source the generator uses for routing and help, so it is always in sync with your code.

```
myapp __schema > cli-schema.json
```

Use cases:

- **LLM / agent tooling** — feed the schema to a language model to give it accurate, structured knowledge of your CLI's commands and options.
- **Generated documentation** — pipe into a docs generator or templating step to keep reference docs in sync without manual maintenance.
- **CI validation** — diff `cli-schema.json` across commits to catch unintentional breaking changes to the CLI surface.

The shape is defined by [`ArghCliSchemaDocument`](src/Nullean.Argh.Core/Schema/ArghCliSchemaDocument.cs). Output is indented camelCase JSON. Reserved meta-commands (`__complete`, `__completion`, `__schema`) appear under `reservedMetaCommands`.

**Native AOT in CI:** The GitHub Actions workflow runs an **`aot-validate`** job that publishes [`examples/ArghAotSmoketest`](examples/ArghAotSmoketest) with Native AOT on Linux, macOS, and Windows and invokes `__schema` on the native binary. That sample uses `Microsoft.Extensions.Hosting` and `AddArgh` so **`Map` / `MapNamespace` DI registration** is included in the AOT publish. The repo uses the SDK unified artifacts layout (output under **`.artifacts/`**, gitignored).

## License and links

- **License**: [MIT](LICENSE)
- **Repository**: [github.com/nullean/argh](https://github.com/nullean/argh)
- **Releases**: [GitHub releases](https://github.com/nullean/argh/releases)

This README is the NuGet package readme for `Nullean.Argh`, `Nullean.Argh.Core`, `Nullean.Argh.Interfaces`, and `Nullean.Argh.Hosting`.
