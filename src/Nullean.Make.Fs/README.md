# Nullean.Make.Fs

Typed, namespaced, exhaustive F# build target DSL. Your targets are DU cases.
Adding a case without handling it is a **compile error**.

```fsharp
#!/usr/bin/env -S dotnet fsi --
#r "nuget: Nullean.Make.Fs"
#r "nuget: Proc.Fs"

open Nullean.Make.Fs
open Proc.Fs

type Target = Clean | Build | Test of TestOptions | Release
and  TestOptions = { Filter: string option }

let app = MakeApp<Target>("my-app", Some "My build pipeline")

app.Bind <| function
    | Clean   -> Make.target [] ""       <| fun _ -> exec { run "dotnet" ["clean"] }
    | Build   -> Make.target [Clean] ""  <| fun _ -> exec { run "dotnet" ["build"; "-c"; "Release"] }
    | Test opts -> Make.target [Build] "Run tests" <| fun _ ->
                       exec { run "dotnet" (["test"] @ (opts.Filter |> Option.map (sprintf "--filter:%s") |> Option.toList)) }
    | Release -> Make.command [Test { Filter = None }] []

exit (app.RunAsync(fsi.CommandLineArgs.[1..]).GetAwaiter().GetResult())
```

```
./build.fsx --help
./build.fsx test
./build.fsx test --filter MyClass
./build.fsx release
./build.fsx release -s        # skip prerequisite tests, run only composes
```

---

## Core concepts

### Targets vs Commands

| | Declared with | Has a body | Has deps | Skippable under `-s` |
|---|---|---|---|---|
| **Target** | `Make.target` | Yes | Yes (`DependsOn`) | Deps skipped |
| **Command** | `Make.command` | No (pure composer) | `requires` + `composes` | `requires` skipped |

**Targets** are atomic steps — they do the work. **Commands** are pipeline entry points — they sequence targets. Run either directly from the command line; commands are what you'd normally call in CI.

### The exhaustive match guarantee

`app.Bind` takes `'TCase -> Definition<'TCase>`. The F# compiler enforces exhaustiveness:

```fsharp
type Target = Clean | Build | Test | Release

app.Bind <| function
    | Clean   -> Make.target [] "" <| fun _ -> ...
    | Build   -> Make.target [Clean] "" <| fun _ -> ...
    | Test    -> Make.target [Build] "" <| fun _ -> ...
    // ⚠  warning FS0025: incomplete pattern matches — Release not handled
```

Add a case to the DU, handle it in `Bind`, ship. The framework refuses to run with an incomplete graph.

### Namespaces via nested DUs

Nest a plain DU inside another case to create CLI namespace groups. The payload type being a union **is** the namespace declaration — no `Make.ns` call needed:

```fsharp
type SchemaTarget = Update | Validate   // sub-DU → becomes CLI namespace
type PkgTarget    = Generate | Validate

type Target =
    | Schema  of SchemaTarget   // routes: schema update, schema validate
    | Pkg     of PkgTarget      // routes: pkg generate,  pkg validate
    | Clean
    | Build
    | Release
```

Adding `SchemaTarget.Lint` is a compile error until `| Schema Lint ->` is handled.

When two sub-DUs share a case name (both have `Validate`), qualify the ambiguous one:

```fsharp
app.Bind <| function
    | Schema Update                  -> Make.target [] "" <| fun _ -> ...
    | Schema SchemaTarget.Validate   -> Make.target [] "" <| fun _ -> ...
    | Pkg Generate                   -> Make.target [] "" <| fun _ -> ...
    | Pkg PkgTarget.Validate         -> Make.target [] "" <| fun _ -> ...
```

---

## API reference

### `MakeApp<'TCase>`

```fsharp
let app = MakeApp<Target>("app-name", Some "Optional description shown in --help")
```

#### Global options

Returned handles are mutable refs populated during arg parsing. Close over them in target bodies.

```fsharp
let verbose = app.Flag("--verbose", short = "-v", desc = "Enable verbose output")
let token   = app.Option<string option>("--token", desc = "API token", defaultValue = None)
```

Read in target bodies via `ctx.IsSet(verbose)` / `ctx.Get(token)`, or directly via `.Value`.

#### `app.Bind`

```fsharp
app.Bind <| function
    | MyTarget -> Make.target [...] "description" <| fun ctx -> ...
    | MyCmd    -> Make.command [...] [...]
```

#### `app.RunAsync`

```fsharp
exit (app.RunAsync(fsi.CommandLineArgs.[1..]).GetAwaiter().GetResult())
```

---

### `Make.target`

```fsharp
Make.target (deps: 'T list) (desc: string) (body: FsContext -> unit) : Definition<'T>
```

- `deps` — targets that must complete first (skipped under `-s`)
- `desc` — shown in `--help`; pass `""` to derive from the case name
- `body` — the work; use `ctx.IsSet` / `ctx.Get` to read global options

```fsharp
| Build ->
    Make.target [Clean] "dotnet build -c Release" <| fun _ ->
        exec { run "dotnet" ["build"; "-c"; "Release"] }

| PristineCheck ->
    Make.target [] "Verify no pending changes" <| fun ctx ->
        if ctx.IsSet(skipCheck) then ()
        else
            let r = exec { binary "git"; arguments ["status"; "--porcelain"]; output }
            if r.ConsoleOut |> Seq.isEmpty |> not then
                failBuild "Checkout has pending changes"
```

### `Make.command`

```fsharp
Make.command (requires: 'T list) (composes: 'T list) : Definition<'T>
```

- `requires` — gate steps, skipped under `-s` (verification, tests)
- `composes` — the actual work, always runs

The `--help` for a command auto-generates its description from the graph:

```
release — pristine-check, test → pkg generate, pkg validate, release-notes
```

```fsharp
| Release ->
    Make.command
        [ PristineCheck; Test defaultTest ]          // requires (gates)
        [ PkgGenerate; PkgValidate; ReleaseNotes ]   // composes (work)
```

### `Make.composer`

Like `Make.command` but with an optional trailing body that runs after all `composes`:

```fsharp
| Publish ->
    Make.composer
        [ Release ]
        [ CreateGithubRelease ]
        (fun ctx ->
            printfn "Published at %s" (DateTime.UtcNow.ToString("o")))
```

### `failBuild`

```fsharp
failBuild (message: string) : 'a
```

Aborts the run with exit code 1. Prefer over raising `MakeException` directly.

---

## Per-target typed arguments

A DU case with a **record payload** binds CLI flags to that record at execution time. The record fields become `--flag` options; `string option` fields are optional.

```fsharp
type TestOptions = { Filter: string option; NoBuild: bool }
let defaultTest  = { Filter = None; NoBuild = false }

type Target =
    | Test of TestOptions
    | ...

app.Bind <| function
    | Test opts ->
        Make.target [Build] "Run all tests" <| fun _ ->
            exec { run "dotnet"
                       (["test"]
                        @ (if opts.NoBuild then ["--no-build"] else [])
                        @ (opts.Filter |> Option.map (sprintf "--filter:%s") |> Option.toList)) }
```

```
./build.fsx test --help

  test — Run all tests

  Usage:
    my-app test [options]

  Options:
    --filter <string?>
    --no-build

  Depends on:
    build
```

```
./build.fsx test
./build.fsx test --filter "MyNamespace.MyClass"
./build.fsx test --no-build --filter "MyClass"
```

---

## The `-s` / `--single-target` flag

Built-in. Skips `DependsOn` deps on targets and `Requires` gates on commands:

```
./build.fsx release -s    # skip pristine-check + test, run only pkg generate, pkg validate, ...
./build.fsx build -s      # skip clean, run build body only
```

---

## Global options via `FsContext`

Target bodies receive an `FsContext` (or `_` if unused):

```fsharp
type FsContext with
    member _.Get(optRef: OptionRef<'T>)    : 'T   // returns current value
    member _.IsSet(optRef: OptionRef<bool>): bool  // true if flag was passed
```

---

## Worked example — full pipeline

```fsharp
#!/usr/bin/env -S dotnet fsi --
#r "nuget: Nullean.Make.Fs"
#r "nuget: Proc.Fs"

open System
open System.IO
open Nullean.Make.Fs
open Proc.Fs

// ── namespace sub-DUs ─────────────────────────────────────────────────────────

type SchemaTarget = Update | Validate
type PkgTarget    = Generate | Validate

// ── target / command DU ───────────────────────────────────────────────────────

type Target =
    | Schema of SchemaTarget          // routes: schema update, schema validate
    | Pkg    of PkgTarget             // routes: pkg generate,  pkg validate
    | Clean
    | Build
    | PristineCheck
    | Test            of TestOptions
    | GenerateReleaseNotes
    | Release
    | Publish

and TestOptions = { Filter: string option }

let defaultTest = { Filter = None }

// ── app + global options ──────────────────────────────────────────────────────

let app          = MakeApp<Target>(fsi.CommandLineArgs.[0], Some "My build pipeline")
let skipCheck    = app.Flag("--clean-checkout", short = "-c", desc = "Skip pristine-checkout guard")
let token        = app.Option<string option>("--token", desc = "GitHub token", defaultValue = None)

// ── binding ───────────────────────────────────────────────────────────────────

app.Bind <| function

    | Schema Update ->
        Make.target [] "" <| fun _ ->
            exec { run "my-schema-tool" ["--out"; "schema/my-schema.json"] }

    | Schema SchemaTarget.Validate ->
        Make.target [] "Fail if schema is out of date" <| fun _ ->
            // diff current schema against committed file …
            failBuild "schema is out of date — run: ./build.fsx schema update"

    | Pkg Generate ->
        Make.target [] "" <| fun _ ->
            exec { run "dotnet" ["pack"; "-c"; "Release"; "-o"; "build/output"] }

    | Pkg PkgTarget.Validate ->
        Make.target [] "" <| fun _ ->
            exec { run "dotnet" ["nupkg-validator"; "build/output/*.nupkg"] }

    | Clean ->
        Make.target [] "" <| fun _ ->
            exec { run "dotnet" ["clean"] }

    | Build ->
        Make.target [Clean] "" <| fun _ ->
            exec { run "dotnet" ["build"; "-c"; "Release"] }

    | PristineCheck ->
        Make.target [] "Verify no pending changes" <| fun ctx ->
            if ctx.IsSet(skipCheck) then ()
            else
                let r = exec { binary "git"; arguments ["status"; "--porcelain"]; output }
                if r.ConsoleOut |> Seq.isEmpty |> not then
                    failBuild "Checkout has pending changes"

    | Test opts ->
        Make.target [Build] "Run all tests" <| fun _ ->
            exec { run "dotnet"
                       (["test"; "-c"; "Release"]
                        @ (opts.Filter |> Option.map (sprintf "--filter:%s") |> Option.toList)) }

    | GenerateReleaseNotes ->
        Make.target [] "" <| fun ctx ->
            let tokenArgs = ctx.Get(token) |> Option.map (fun t -> ["--token"; t]) |> Option.defaultValue []
            exec { run "dotnet" (["release-notes"; "my-org"; "my-repo"] @ tokenArgs) }

    | Release ->
        Make.command
            [ PristineCheck; Test defaultTest ]
            [ Pkg Generate; Pkg PkgTarget.Validate; GenerateReleaseNotes ]

    | Publish ->
        Make.command [ Release ] []

exit (app.RunAsync(fsi.CommandLineArgs.[1..]).GetAwaiter().GetResult())
```

### Help output

```
./build.fsx --help

my-app
  My build pipeline

  Usage:
    my-app <command|target> [options]
    my-app <namespace> <target> [options]

  Commands:  (pipeline entry points — compose and sequence targets)
  release    pristine-check, test → pkg generate, pkg validate, generate-release-notes
  publish    release

  Targets:  (atomic steps — can also be run directly)
  clean                    clean
  build                    build  (depends on: clean)
  pristine-check           Verify no pending changes
  test                     Run all tests  (depends on: build)
  generate-release-notes   generate-release-notes

  Namespaces:
  pkg     
  schema  

  Global options:
    -s, --single-target       Skip prerequisite deps; run only the body / Composes
    -h, --help                Show this help
    -c, --clean-checkout      Skip pristine-checkout guard
        --token <string>      GitHub token
```

---

## Process execution with Proc.Fs

`Nullean.Make.Fs` has no process abstraction of its own. The examples use [`Proc.Fs`](https://github.com/nullean/proc) which provides an idiomatic F# CE:

```fsharp
#r "nuget: Proc.Fs"
open Proc.Fs

// Fire and forget
exec { run "dotnet" ["build"; "-c"; "Release"] }

// Capture output
let r = exec { binary "dotnet"; arguments ["minver"]; output }
let version = r.ConsoleOut |> Seq.head |> fun l -> l.Line

// Dynamic argument list
exec { run "dotnet" (["test"] @ filterArgs @ loggerArgs) }
```

Any other process library works equally well — `Proc.Fs` is not a requirement.

---

## Installation

```
#r "nuget: Nullean.Make.Fs"
```

Requires .NET 8+. The package pulls in `Nullean.Make` (the underlying C# engine) transitively.
