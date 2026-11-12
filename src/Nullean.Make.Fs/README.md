# Nullean.Make.Fs

![Nullean.Argh](https://raw.githubusercontent.com/nullean/argh/main/docs/images/png/argh-lockup-348x192.png)

Typed, namespaced, exhaustive F# build targets. Your targets are DU cases, and a case you forget to handle in `Bind` is a compiler warning (FS0025). Target blocks are [Proc.Fs](https://www.nuget.org/packages/Proc.Fs) `shell { }` blocks, so commands are `sh $"…"` lines.

[![NuGet](https://img.shields.io/nuget/v/Nullean.Make.Fs?style=flat-square)](https://www.nuget.org/packages/Nullean.Make.Fs)

## Install

```sh
dotnet add package Nullean.Make.Fs
```

Requires .NET 11. The package pulls in `Nullean.Make` (the underlying C# engine) and `Proc.Fs` transitively.

## Quick start

```fsharp
open Nullean.Make.Fs
open Proc.Fs

type TestOptions = { Filter: string option }

type PkgTarget =
    /// Pack the NuGet packages
    | Generate
    /// Validate the packed NuGet packages
    | Validate

type Target =
    /// Clean known output locations
    | Clean
    /// Run build
    | Build
    /// Run build then tests
    | Test of TestOptions
    | Pkg of PkgTarget
    /// Test, then pack and validate
    | Release

[<EntryPoint>]
let main argv =
    let app    = MakeApp<Target>("build", None)
    let config = app.Option<string>("--config", desc = "Build configuration", defaultValue = "Release")

    app.Bind <| function
        | Clean        -> target [] { sh $"dotnet clean" }
        | Build        -> target [Clean] { sh $"dotnet build -c {config.Value}" }
        | Test o       -> target [Build] {
                              match o.Filter with
                              | Some f -> sh $"dotnet test -c {config.Value} --filter:{f}"
                              | None   -> sh $"dotnet test -c {config.Value}" }
        | Pkg Generate -> target [] { sh $"dotnet pack -c Release -o build/output" }
        | Pkg Validate -> target [] { sh $"dotnet nupkg-validator build/output" }
        | Release      -> command {
                              requires [Test { Filter = None }]
                              composes [Pkg Generate; Pkg Validate] }

    app.RunAsync(argv).GetAwaiter().GetResult()
```

```
build --help
build test --filter Unit --config Debug
build pkg --help
build release
build release -s        # skip the tests, run only pkg generate, pkg validate
```

```
  Commands:  (pipeline entry points — compose and sequence targets)
  release    Test, then pack and validate

  Targets:  (atomic steps — can also be run directly)
  clean    Clean known output locations
  build    Run build  (depends on: clean)
  test     Run build then tests  (depends on: build)

  Namespaces:
  pkg
```

The package depends on `Proc.Fs` 0.17, so `open Proc.Fs` brings in `sh`, the captures and `exec { }` without another reference.

---

## Targets: `target [deps] { ... }`

`target [Clean] { ... }` declares a target that runs after `Clean`; `-s` / `--single-target` skips the dependencies. The block is a Proc.Fs `shell { }` block that runs only when the target does:

```fsharp
| Info -> target [] {
      let! sha = text $"git rev-parse --short HEAD"     // standard out as one trimmed string
      let! files = lines $"git ls-files"                 // standard out as a list of lines
      for file in files |> List.filter (fun f -> f.EndsWith ".fs") do
          sh $"dotnet fantomas {file}"                   // {file} is always one argument
      printfn $"formatted commit {sha}" }
```

| Inside `target [] { }` | Does |
|---|---|
| `sh $"…"` | runs the command; a failure fails the target |
| `let! s = text $"…"` | standard out as one trimmed string |
| `let! ls = lines $"…"` | standard out as a list of lines |
| `let! code = exitCode $"…"` | the exit code, without failing |
| `let! ok = succeeds $"…"` | whether the exit code is `0`, without failing |
| `cd`, `pushd`, `popd`, `env`, `setEnvs`, `unsetEnv` | change the commands that follow |
| `use! _ = inDir "src"`, `use! _ = withEnv [...]` | change them until the end of the enclosing scope |

Plain F# works too: `if`, `match`, loops, `printfn`, and `exec { }` from earlier versions. `sh` splits only the text you wrote, and each `{value}` stays one argument whatever it contains, so no value can start another command. See the [Proc.Fs README](https://github.com/nullean/proc/tree/main/src/Proc.Fs) for quoting, pipes (`|`) and multi-line commands.

The first command that fails fails the target, and `RunAsync` returns `1`.

## Commands: `command { ... }`

```fsharp
| Release -> command {
      requires [PristineCheck; Test { Filter = None }]   // gates, skipped under -s
      composes [Pkg Generate; Pkg Validate] }            // the work, always runs
```

`concurrently [...]` adds a stage whose targets run side by side (F# reserves `parallel`). Stages run in the order
they are written:

```fsharp
| Release -> command {
      requires [Test { Filter = None }]
      concurrently [Pkg Generate; Docs Build]
      composes [Publish] }
```

`-j` / `--jobs` caps how many targets run at once, and `-k` / `--keep-going` lets the targets that do not depend on a
failure finish. `printfn` output of targets running side by side carries their names; `sh` commands keep the console.
See [Running targets side by side](https://argh.nullean.net/make/parallel).

`executes (...)` adds a body that runs after every composed target. Like a target block, it is deferred. Use `shell { }` inside it to run commands:

```fsharp
| Publish -> command {
      requires [Release]
      composes [CreateGithubRelease]
      executes (
          shell {
              let! sha = text $"git rev-parse --short HEAD"
              printfn $"published {sha}" }) }
```

## Conditions, ordering and cleanup

Pipe a target or command through these:

```fsharp
| StartDb -> target [] { sh $"docker compose up -d db" } |> cleanup [StopDb]   // at the end, even after a failure
| Package -> target [] { sh $"dotnet pack" } |> after [Lint]                    // after lint when both run
| Publish -> target [] { sh $"dotnet nuget push" }
             |> onlyWhen (fun () -> not dryRun.Value)   // skip the body; the dependencies still run
             |> needsEnv ["NUGET_API_KEY"]               // checked before anything starts
             |> needsTool ["docker"]
             |> describe "Push the packages"             // for an .fsx script without an XML doc file
```

See [Conditions, ordering and cleanup](https://argh.nullean.net/make/modifiers).

## Descriptions from doc comments

A target's description is the `///` doc comment on its DU case. The comment is read from the XML documentation file, so the build project needs `<GenerateDocumentationFile>true</GenerateDocumentationFile>`. A `.fsx` script produces no documentation file. In a script, pipe the target through `describe "..."`, or the target shows its name.

## Namespaces via nested DUs

A case whose payload is another DU becomes a CLI namespace: `Pkg of PkgTarget` gives `pkg generate` and `pkg validate`, and `pkg --help` lists them. Two sub-DUs may share case names, such as `Validate`; qualify the ambiguous one in `Bind` (`| Schema SchemaTarget.Validate ->`).

## Per-target typed arguments

A case with a record payload binds the record's fields from CLI flags: `Test of TestOptions` with `{ Filter: string option }` takes `--filter`. A dependency can name any value of the case: `Test { Filter = Some "Unit" }` runs the `test` target with that value. Two dependencies with equal values run the target once, and with different values once each. Only the target named on the command line binds the command line's flags.

### Positional arguments, forwarded arguments and `--name=value`

Mark a record field `[<Argument>]` to take a positional token; a `'T list` field marked `[<Argument>]` takes every remaining positional. Mark a `'T list` field `[<Rest>]` to receive everything after a `--`:

```fsharp
open Nullean.Argh                            // [<Argument>]; [<Rest>] is in Nullean.Make.Fs

type TestOptions =
    { [<Argument>] Projects: string list     // build test src/A src/B
      Suite: string option                   // --suite unit  or  --suite=unit
      [<Rest>] Rest: string list }           // build test -- --treenode-filter "/*/*/MyTests/*"

| Test o -> target [] { sh $"dotnet test {o.Rest}" }   // the list becomes separate arguments
```

A token no field takes exits `2` with `Unexpected argument 'x'.`, and so does any argument for a target without a record.

## Hidden steps

`step [Deps] { ... }` is `target` left out of help. It still runs when named and when another target depends on it. `Make.step [] (fun ctx -> ...)` is the function form.

## The default target

`app.Default Build` names the target that runs when no target is given, so `./build.sh` builds. Help shows it. `-s` without a target still prints help. `--version` prints the entry assembly's informational version.

## Global options

```fsharp
let verbose = app.Flag("--verbose", short = "-v", desc = "Enable verbose output")
let token   = app.Option<string option>("--token", desc = "API token", defaultValue = None)
```

Read them in target blocks through `.Value`. Help shows each option's type, such as `--config <string>`, and `--token <string?>` for an option type. Both `--token abc` and `--token=abc` work. A value that does not parse, or a missing value, exits `2` before any target runs. Enum options bind by name, ignoring case.

## Drawing the plan

`--graph` draws what a run would do and runs nothing. It binds the target's arguments, applies `-s` and evaluates
conditions, so a skipped target shows as skipped:

```
build release -s --graph
build release --graph mermaid      # or json
```

A target's `--help` lists the order its dependencies run in under `Runs:`. See
[Drawing the plan](https://argh.nullean.net/make/graph).

## mise, make and just

`__wrappers sync` writes a mise task include, a Makefile include and a justfile import with one task per target, so
`mise run test`, `make test` and `just test` call the build. A project-based build passes the command to run once;
later syncs reuse it:

```
dotnet run --project build -- __wrappers sync --run "dotnet run --project build --"
```

The files go to `.make/`, and `sync` prints the line each wrapper's config needs, plus mise hooks that regenerate the
files so they can be gitignored. The [Nullean.Make README](https://github.com/nullean/argh/blob/main/src/Nullean.Make/README.md#mise-make-and-just)
covers the options, the task names (`pkg:generate`, `pkg/generate`, `pkg-generate`) and `__wrappers check`; the
[docs](https://argh.nullean.net/make/wrappers) have a page per wrapper.

## The explicit functions

`target` and `command` are shorthand for these, which also take a description and give the body an `FsContext`:

```fsharp
Make.target  (deps: 'T list) (desc: string) (body: FsContext -> unit)
Make.command (requires: 'T list) (composes: 'T list)
Make.composer (requires: 'T list) (composes: 'T list) (body: FsContext -> unit)   // like command { executes (...) }
failBuild "message"                                                               // abort with exit code 1
```

## Learn more

- Documentation: https://argh.nullean.net/make
- Source and issues: https://github.com/nullean/argh
