# Nullean.Argh — Project Vision & Design Decisions

A source-generated, AOT-compatible CLI framework for .NET. Similar to [Cysharp/ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) but fixing several pain points and adding missing features.

## Motivation

ConsoleAppFramework has gaps in:
- XML doc rendering (tags rendered literally, no `<summary>` vs `<remarks>` distinction)
- Group/help scoping (`--help` shows all commands regardless of depth)
- Collection syntax (no user choice)
- Missing features: shell completions, `IAsyncEnumerable` output (follow-up)

The author also authored [PR #237](https://github.com/Cysharp/ConsoleAppFramework/pull/237) (`[AsParameters]` + typed `ConfigureGlobalOptions<T>()`) which was not merged — these are first-class features in argh.

---

## Packages

| Package | Dependencies | TFMs |
|---------|-------------|------|
| `Nullean.Argh` | Zero runtime deps | `netstandard2.0` |
| `Nullean.Argh.Hosting` | `Microsoft.Extensions.{Hosting,Logging,Options,DependencyInjection}` | `netstandard2.0` + `net8.0`, `net9.0`, `net10.0` |

- Source generator ships **inside** `Nullean.Argh` (via `analyzers/dotnet/cs` NuGet folder)
- Parse/bind test helpers fold into `Nullean.Argh`

**Hard requirement: NativeAOT compatible. No reflection. Everything source-generated.**

---

## Registration Model

Explicit only — no assembly scanning.

Two supported styles:

```csharp
// A: Lambda / static method
app.Add("deploy", (string env, int replicas) => { });

// C: Grouped methods on a class
app.Add<DevOpsCommands>(); // all public methods become subcommands
```

No class-per-command as primary model. `[AsParameters]` handles binding shape separately.

### Group nesting — ASP.NET MapGroup style

```csharp
app.Group("storage", g => {
    g.Add<BlobCommands>();
    g.Group("archive", a => {
        a.Add<ArchiveCommands>();
    });
});
```

Nested classes within a registered type implicitly form subgroups:

```csharp
class StorageCommands {
    public Task List() { }       // → storage list
    class BlobCommands {
        public Task Upload() { } // → storage blob upload
    }
}
```

Arbitrary nesting depth supported.

---

## Naming Conventions

Everything is **kebab-case by default**, overridable via XML docs or attributes.

| Source | Convention | Example |
|--------|-----------|---------|
| Class name | Strip configurable suffixes → kebab-case | `DevOpsCommands` → `dev-ops` |
| Method name | kebab-case | `Deploy` → `deploy` |
| Parameter name | kebab-case | `targetEnv` → `--target-env` |

**Suffix stripping**: configurable, defaults include `Commands`, `Command`, `Handler` (singular + plural). Never strips if result would be empty.

Result: `DevOpsCommands.Deploy(string targetEnv)` → `myapp dev-ops deploy --target-env`

---

## Parameters

### Kinds

| Kind | Syntax | Notes |
|------|--------|-------|
| Named flag | `--flag value` | Default |
| Positional | `[Argument]` attribute | Must be successive from position 0; compile error otherwise |
| `bool` flag | `--verbose` | Presence = true, no value needed |
| `bool?` flag | `--dry-run` / `--no-dry-run` | null = default |
| Short opt | `-e` | Explicit in XML docs only |
| Collection | user-declared | Space-separated OR repeated flag, chosen via XML docs/attribute |

**Required**: non-nullable + no default = required (except `bool` flags).

### Type Support

- Primitives (`string`, `int`, `double`, etc.)
- Enums — parsed as strings, case-insensitive, auto-documented in help
- `FileInfo`, `DirectoryInfo`, `Uri`
- `IArgumentParser<T>` interface for custom types
- `CancellationToken` — auto-injected, wired to Ctrl+C, not a CLI arg
- No JSON parsing (explicit non-feature)

### [AsParameters] — Class-Based Binding

Bind command parameters to a record/class. Independent of command registration.

```csharp
public record DeployConfig(
    [Argument] string Env,   // positional [0]
    int Replicas = 1         // optional flag
);

app.Add("deploy", ([AsParameters] DeployConfig config) => { });
// → myapp deploy <env> [--replicas <int>]
```

Supports prefixes for multiple instances on one command:

```csharp
app.Add("copy", (
    [AsParameters("source")] DbConfig source,
    [AsParameters("target")] DbConfig target) => { });
// → myapp copy --source-host db1 --target-host db2
```

---

## Global and Group Options

```csharp
app.GlobalOptions<GlobalOptions>();

app.Group("storage", g => {
    g.GroupOptions<StorageOptions>(); // compile error if StorageOptions doesn't extend GlobalOptions
    g.Add<BlobCommands>();
});
```

- `GroupOptions<T>` enforces `T : ParentOptionsType` at source-gen time
- Commands in a group receive their group's options type (transitively includes global)
- Explicit `.GlobalOptions<>()` and `.GroupOptions<>()` methods — no implicit magic

---

## Help Text

**XML docs are the only way to configure help output.**

| XML tag | Renders as |
|---------|-----------|
| `<summary>` | One-liner in parent command list |
| `<remarks>` | Full description in command's own `--help` |
| `<param name="x">-e,--environment, description` | Flag description, short opt, long alias |
| `<example>` | Usage examples block (structure TBD) |
| `<inheritdoc>` | Inherited from base class/interface |
| `<para>`, `<code>`, `<list>` | Rendered properly, not literally |

Positional args (`[Argument]`) cannot have short opts or long aliases in their `<param>` tag.

### Usage line format

```
Usage: myapp deploy <env> [<replicas>] --tag <string> [options]
```

- Required positional: `<arg>`
- Optional positional: `[<arg>]`
- Required flags: spelled out explicitly in synopsis
- Optional flags: collapsed to `[options]`

### Options table

```
Arguments:
  <env>          Target environment. 
  [<replicas>]   Number of replicas. [default: 1]

Options:
  --tag <string>               [required] Tag to apply.
  -e, --environment <string>   Alias for env flag.
  --verbose                    Enable verbose output.
  --dry-run / --no-dry-run     Simulate without applying changes.
  --help                       Show help.
  --version                    Show version.
```

- `[required]` prefix in description column for required flags
- Defaults shown for all args/flags: `[default: value]`
- ANSI color by default, respects `NO_COLOR` env var and non-TTY detection

### `--help` scoping

Boundary-scoped at every depth:
- `myapp --help` → top-level commands/groups only
- `myapp storage --help` → storage subcommands only
- `myapp storage blob --help` → blob commands only

### Enum documentation

```
--target <string>   Description. [values: file, network]
                      file     XML doc summary of File value.
                      network  XML doc summary of Network value.
```

Per-value docs only shown if enum members have XML docs. Falls back to `[values: file, network]`.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | Unhandled exception / runtime error |
| `2` | CLI usage error (bad args, unknown command, missing required flag) |

Unhandled exception handler: default ships (print full exception, exit 1). Users register custom handler; rethrowing is documented.

---

## Error Messages

- All parse errors → **stderr**
- Auto-print relevant `--help` after parse error
- Exit code `2` for all parse errors
- Fuzzy "did you mean?" on unknown commands:
  - Single match → print that command's full `--help`
  - Multiple matches → list matches with summaries

```
Error: unknown command 'dploy'. Did you mean one of these?

  deploy    Deploy application to an environment.
  destroy   Destroy a deployed environment.

Run 'myapp <command> --help' for usage.
```

---

## Filters (Post-routing)

Single filter model, runs after command is matched. Pipeline: `filter1 → filter2 → command → filter2 → filter1`.

```csharp
// Global inline filter
app.UseFilter(async (ctx, next) => {
    await next();
});

// Typed filter (DI injectable in .Hosting)
class MyFilter : ICommandFilter {
    public async ValueTask InvokeAsync(CommandContext ctx, CommandFilterDelegate next) {
        // before
        await next(ctx);
        // after
    }
}

// Per-command via attribute
[Filter<MyFilter>]
public Task Deploy(string env) { }
```

---

## Built-in Flags

- `--help` / `-h`: boundary-scoped help, every depth level
- `--version`: auto-generated from assembly version

---

## Shell Completions

Auto-completion script generation for bash, zsh, fish. Not present in ConsoleAppFramework — a differentiating feature. Exact design TBD.

---

## .Hosting Package

Registration extends the existing host builder (ASP.NET-native feel):

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddArgh(args, app => {
    app.Add<DeployCommands>();
});
await builder.Build().RunAsync();
```

- Constructor injection for command classes
- **Transient by default**, opt-in scoped per invocation
- `ILogger<T>`, `IOptions<T>`, etc. via standard constructor injection — no magic
- `IOptions<T>` for flag defaults lives outside argh — user registers on host

---

## Testing

Commands are plain injectable classes — unit test by direct instantiation, no framework needed:

```csharp
var cmd = new DeployCommands(mockService);
await cmd.Deploy("production", 3);
```

Base package exposes parse/bind helpers for CLI-layer tests:

```csharp
// Bind args to a parameter record
var bound = ArghParser.Bind<DeployConfig>("--env production --replicas 3");
bound.Env.Should().Be("production");

// Full route + execute
var result = await app.RunAsync("deploy --env production");
result.ExitCode.Should().Be(0);
```

---

## Explicit Non-Features (v1)

- No JSON parsing of complex types
- No response files (`@file.txt`)
- No env var → flag magic (users handle via `IOptions` + DI)
- No `CommandLineConfigurationProvider` deep integration
- No `IAsyncEnumerable<string>` return type *(possible v2 follow-up)*

---

## Inspired By / Improving On

| Issue/PR | Problem | Argh solution |
|----------|---------|---------------|
| [#230](https://github.com/Cysharp/ConsoleAppFramework/issues/230) | No `<summary>` vs detailed description distinction | `<summary>` = one-liner, `<remarks>` = full detail |
| [#231](https://github.com/Cysharp/ConsoleAppFramework/issues/231) | XML tags rendered literally | Proper `<para>`, `<code>`, `<list>` rendering |
| [#234](https://github.com/Cysharp/ConsoleAppFramework/issues/234) | Enums not auto-documented | Inline `[values: ...]` + per-member XML docs |
| [#242](https://github.com/Cysharp/ConsoleAppFramework/issues/242) | `--help` shows all commands regardless of depth | Boundary-scoped `--help` |
| [#237](https://github.com/Cysharp/ConsoleAppFramework/pull/237) | No class-based parameter binding | First-class `[AsParameters]` |
| [#239](https://github.com/Cysharp/ConsoleAppFramework/pull/239) | No repeated flag for collections | User-declared collection syntax |
