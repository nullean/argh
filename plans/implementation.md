# Nullean.Argh — Implementation Plan

Phased breakdown. Each phase should be shippable/testable before moving to the next.
See `plans/vision.md` for design decisions behind each choice.

---

## Phase 1: Project Structure & Generator Scaffolding

**Goal:** Both packages compile, generator runs (even if it generates nothing useful yet), CI passes.

### Tasks

- [ ] Set up `src/Nullean.Argh/Nullean.Argh.csproj`
  - `netstandard2.0`
  - `<IsRoslynComponent>true</IsRoslynComponent>`
  - Reference `Microsoft.CodeAnalysis.CSharp` as PrivateAssets=all
  - Pack generator into `analyzers/dotnet/cs` folder
- [ ] Set up `src/Nullean.Argh.Hosting/Nullean.Argh.Hosting.csproj`
  - Multi-target: `netstandard2.0;net8.0;net9.0;net10.0`
  - Reference `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Options`
- [ ] Set up `tests/Nullean.Argh.Tests/` (xunit, FluentAssertions — already present)
- [ ] Confirm `IIncrementalGenerator` skeleton wires up correctly (empty Initialize)
- [ ] Add a sample console app under `samples/` to validate end-to-end from the start

### Notes

- Generator and runtime types live in the same assembly. The generator references `Microsoft.CodeAnalysis`; the runtime types do not. Use `#if ROSLYN` guards or split into partial classes if needed.
- The NuGet pack structure needs `buildTransitive` props/targets to wire up the analyzer reference automatically for consumers.

---

## Phase 2: Internal Generator Model

**Goal:** Generator can parse user registration call sites into a structured in-memory model. No code emitted yet — just the analysis layer.

### Model types (generator-internal)

```
AppModel
  └── GroupModel (recursive)
        ├── name: string
        ├── options type: ITypeSymbol?
        ├── commands: CommandModel[]
        └── groups: GroupModel[]

CommandModel
  ├── name: string (derived or explicit)
  ├── method: IMethodSymbol
  ├── parameters: ParameterModel[]
  └── xmlDocs: CommandDocs

ParameterModel
  ├── name: string (derived)
  ├── cliName: string (kebab-case)
  ├── kind: Flag | Positional | Injected
  ├── type: ITypeSymbol
  ├── isRequired: bool
  ├── defaultValue: string?
  ├── shortOpt: char?
  ├── aliases: string[]
  └── xmlDocs: string
```

### Tasks

- [ ] `CliParserGenerator` — find all `app.Add(...)`, `app.Add<T>()`, `app.Group(...)` call sites via syntax receiver
- [ ] Extract method symbols from `Add<T>()` (all public methods on T) and lambda `Add("name", lambda)`
- [ ] Derive command name: strip configurable suffixes + kebab-case (configurable suffix list in `[assembly: ArghDefaults(...)]` attribute or MSBuild property)
- [ ] Derive parameter kind: positional (`[Argument]`), injected (`CancellationToken`), flag (everything else)
- [ ] Determine required: non-nullable + no default value (except `bool`)
- [ ] XML doc extraction from `ISymbol.GetDocumentationCommentXml()`
- [ ] Parse `<param>` tag for `-e,--environment, description` format
- [ ] Parse `<summary>` and `<remarks>` separately
- [ ] Validate: `[Argument]` parameters must be successive from position 0 → emit `Diagnostic` error if not
- [ ] Model `app.Group(...)` nesting, including nested classes

---

## Phase 3: Code Generation — Flat Commands, Primitives

**Goal:** A working CLI with flat (non-grouped) commands, primitive parameters, basic help, correct exit codes. The first genuinely usable milestone.

### Generated structure

```csharp
// Generated partial class wiring into the app builder
internal static partial class ArghGenerated
{
    public static async Task<int> RunAsync(string[] args)
    {
        // 1. Check for --help / --version at root
        // 2. Route to command by args[0]
        // 3. Parse flags for matched command
        // 4. Call command method
        // 5. Return exit code
    }
}
```

### Tasks

- [ ] Emit routing switch on `args[0]` → matched command
- [ ] Emit per-command argument parser (generated, no reflection):
  - Walk `args[1..]`, match `--flag value` pairs
  - Assign positional args by order
  - Validate required args present
  - Parse primitives: `string`, `int`, `long`, `double`, `float`, `decimal`, `bool`
- [ ] `--help` at root → print command list (one-liner from `<summary>`) + exit 0
- [ ] `--help` after command name → print command help + exit 0
- [ ] `--version` → print assembly version + exit 0
- [ ] Unknown command → print error to stderr + exit 2
- [ ] Missing required arg → print error to stderr + exit 2
- [ ] Wrong type for flag → print error to stderr + exit 2
- [ ] Unhandled exception handler: default prints full exception to stderr + exit 1; hookable
- [ ] `CancellationToken` injection wired to `Console.CancelKeyPress`
- [ ] `int` / `Task<int>` return type → use as exit code; `void` / `Task` → exit 0

---

## Phase 4: Help Text Rendering

**Goal:** Help output is complete, well-formatted, ANSI-colored, and matches the spec.

### Tasks

- [ ] Usage line: `Usage: myapp <command> [options]` at root; `Usage: myapp deploy <env> [<replicas>] --req-flag <string> [options]` per command
  - Required positional: `<arg>`
  - Optional positional: `[<arg>]`
  - Required flags: spelled out explicitly
  - Optional flags: `[options]`
- [ ] Arguments table: positional args with descriptions and `[default: x]`
- [ ] Options table:
  - `[required]` prefix in description for required flags
  - `[default: x]` suffix
  - `-e, --alias` rendering
  - `--dry-run / --no-dry-run` for `bool?`
- [ ] `<remarks>` shown as extended description below summary in command help
- [ ] XML tag rendering: strip/render `<para>` as newline, `<code>` as indented block, `<list>` as bullet list
- [ ] ANSI color output (command names, arg placeholders, section headers)
- [ ] Detect `NO_COLOR` env var and non-TTY (`Console.IsOutputRedirected`) → plain text fallback
- [ ] Boundary-scoped `--help`: `--help` mid-args routes to nearest matched group/command

---

## Phase 5: Full Parameter Type Support

**Goal:** All planned parameter types work correctly.

### Tasks

- [ ] `bool` flag — presence = true, no value consumed
- [ ] `bool?` — generates `--name` and `--no-name`; null = default
- [ ] `[Argument]` positional — compile-time validation of successive ordering
- [ ] Enum parsing — case-insensitive string match; emit diagnostic if value not valid
- [ ] Enum help documentation — `[values: a, b, c]` in options table; per-member XML doc lines if present
- [ ] `FileInfo`, `DirectoryInfo` — parse from string path
- [ ] `Uri` — parse from string
- [ ] `IArgumentParser<T>` — generated call to `parser.TryParse(string, out T)` (user implements, registered via attribute or convention TBD)
- [ ] Short opts: `-e value` and `-e=value` parsing
- [ ] Long alias: `--environment` as synonym for `--env`

---

## Phase 6: Groups & Nested Commands

**Goal:** Full group nesting works. `--help` is boundary-scoped at every level.

### Tasks

- [ ] `app.Group("name", g => { ... })` registration at source-gen time
- [ ] Nested `app.Group()` within a group
- [ ] Nested class within registered type → implicit subgroup (name derived same as class)
- [ ] Routing: walk args left-to-right matching group names before command names
- [ ] Boundary-scoped `--help`:
  - `myapp --help` → root groups + top-level commands only
  - `myapp storage --help` → storage's direct children only
  - Each level auto-appends its own `--help` to the options table

---

## Phase 7: Global & Group Options

**Goal:** Typed options flow through the command hierarchy with compile-time inheritance enforcement.

### Tasks

- [ ] `app.GlobalOptions<T>()` — T parsed before routing; available to all commands
- [ ] `app.Group(..., g => { g.GroupOptions<T>(); })` — T extends parent options type
- [ ] Compile-time diagnostic: `GroupOptions<T>` where T does not extend the parent group's options type
- [ ] Generated code: parse global options first, then group options (additive), then command args
- [ ] Group options appear in `--help` for that group under a "Group Options:" section
- [ ] Global options appear in root help under "Global Options:" section; excluded from per-command option tables (to avoid duplication)

---

## Phase 8: [AsParameters] — Class-Based Binding

**Goal:** Record/class parameter binding works, including prefix support and interaction with positional args.

### Tasks

- [ ] Detect `[AsParameters]` on a parameter of a command method/lambda
- [ ] Analyze the record/class: primary constructor params and `init` properties
- [ ] Flatten into `ParameterModel[]` — same pipeline as inline params
- [ ] `[property: Argument]` on record property → positional
- [ ] `[AsParameters("prefix")]` → prepend `prefix-` to all flag names
- [ ] Multiple `[AsParameters]` on one method with different prefixes
- [ ] Compile-time diagnostic if prefix-less `[AsParameters]` would produce duplicate flag names
- [ ] XML docs on the record's constructor params / properties used for help
- [ ] `[AsParameters]` type can inherit from the registered global/group options type → values auto-populated

---

## Phase 9: Collections

**Goal:** Users can declare collection syntax per-parameter.

### Tasks

- [ ] Detect `IEnumerable<T>`, `T[]`, `List<T>`, `IReadOnlyList<T>` parameter types
- [ ] Default: repeated flag (`--tag foo --tag bar`) — safest default
- [ ] Override via XML docs custom tag: `<separator>,</separator>` → parse as `--tag foo,bar`
- [ ] Override via attribute (name TBD) on the parameter
- [ ] Compile-time diagnostic if collection param has no declared syntax and default is ambiguous

---

## Phase 10: Filters

**Goal:** Pre/post command execution hooks, global and per-command.

### Tasks

- [ ] Define `ICommandFilter` interface and `CommandContext` type in runtime assembly
- [ ] `app.UseFilter(async (ctx, next) => { })` — global inline filter
- [ ] `app.UseFilter<T>()` — global typed filter
- [ ] `[Filter<T>]` attribute on a command method → per-command filter
- [ ] Generated pipeline: wrap command call in filter chain (innermost = command)
- [ ] `CommandContext` exposes: `CommandName`, `Args`, `ExitCode` (settable), `CancellationToken`
- [ ] Filters run post-routing (command is already matched)
- [ ] In `.Hosting`: typed filters resolved from DI

---

## Phase 11: Error UX — Fuzzy Matching

**Goal:** Unknown commands get helpful suggestions with scoped help.

### Tasks

- [ ] Implement Levenshtein distance (or similar) on command names — generated into the runtime, no external dep
- [ ] Unknown command: compute closest match(es) within threshold
  - Single match → print that command's full `--help` to stderr
  - Multiple matches → print match list with summaries to stderr
- [ ] "Run 'myapp `<command>` --help' for usage." hint line
- [ ] Apply fuzzy matching at every group level (not just root)

---

## Phase 12: Shell Completions

**Goal:** Users can generate completion scripts for bash, zsh, fish.

### Tasks

- [ ] Built-in `--completions <shell>` flag (bash | zsh | fish) at root
- [ ] Generated completion script covers: command names, group names, flag names, enum values
- [ ] Fish: `complete -c myapp -n '__fish_use_subcommand' -a deploy -d 'Deploy application'`
- [ ] Bash/zsh: standard `_myapp()` completion function
- [ ] Completions are fully static — generated at compile time into the binary, printed on request
- [ ] Document how to wire into shell profile

---

## Phase 13: Nullean.Argh.Hosting

**Goal:** Deep Microsoft.Extensions.* integration works seamlessly.

### Tasks

- [ ] `AddArgh(string[] args, Action<IArghBuilder> configure)` extension on `IServiceCollection`
- [ ] `IArghBuilder` wraps the registration API (same model as base, resolves types from DI)
- [ ] Command classes resolved from DI (transient default)
- [ ] Opt-in scoped per invocation: `app.UseScoped()` or per-command attribute
- [ ] Constructor injection for all registered DI services
- [ ] Wire `IHostApplicationLifetime.ApplicationStopping` → `CancellationToken` (replaces `Console.CancelKeyPress`)
- [ ] Generated `IHostedService` that runs the CLI and calls `IHostApplicationLifetime.StopApplication()` on completion
- [ ] Filters resolved from DI when typed
- [ ] Sample showing `ILogger<T>`, `IOptions<T>` via constructor injection

---

## Phase 14: Parse/Bind Test Helpers

**Goal:** Users can write integration tests against the CLI parsing layer without spawning a process.

### Tasks

- [ ] `ArghParser.Bind<TParams>(string args)` → parses args string into the parameter record/type
- [ ] `ArghParser.Route(string args)` → returns matched command name + unmatched args
- [ ] `app.RunAsync(string args)` overload → splits string, runs, returns `RunResult { ExitCode, Stdout, Stderr }`
- [ ] `RunResult` captures console output written during execution
- [ ] Document: for pure unit testing, just instantiate the command class directly

---

## Follow-up / Post-v1

- `IAsyncEnumerable<string>` return type → print each line as yielded
- `CommandLineConfigurationProvider` integration story (if use cases emerge)
- `IOptions<T>` as CLI arg defaults (config-driven defaults, overridable by CLI)
- NuGet source link + deterministic builds
- Benchmarks vs ConsoleAppFramework

---

## Key Risks & Notes

| Risk | Mitigation |
|------|-----------|
| Source generator finding `app.Add<T>()` call sites reliably across complex code | Start with simple syntax-based detection; use semantic model for type resolution; add integration tests for edge cases (aliased variable names, method chaining) |
| AOT validation | Add NativeAOT test project early (Phase 3); failures are hard to retrofit |
| XML doc availability at source-gen time | XML docs require `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in the consumer's project; emit a helpful warning if missing |
| `netstandard2.0` generator with `net10.0` APIs | Generator runs in the compiler process — only use `netstandard2.0`-compatible Roslyn APIs |
| Incremental generator correctness | Every `IncrementalValuesProvider` transform must be value-comparable; use records or manual `IEquatable` for model types to avoid full regeneration on every keystroke |
