# Nullean.Make.Fs

Typed, namespaced, exhaustive F# build targets. Your targets are DU cases, and a case you forget to handle in `Bind` is a compiler warning (FS0025).

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
        | Clean        -> target [] { exec { run "dotnet" "clean" } }
        | Build        -> target [Clean] { exec { run "dotnet" ["build"; "-c"; config.Value] } }
        | Test o       -> target [Build] {
                              match o.Filter with
                              | Some f -> exec { run "dotnet" ["test"; "-c"; config.Value; $"--filter:{f}"] }
                              | None   -> exec { run "dotnet" ["test"; "-c"; config.Value] } }
        | Pkg Generate -> target [] { exec { run "dotnet" ["pack"; "-c"; "Release"; "-o"; "build/output"] } }
        | Pkg Validate -> target [] { exec { run "dotnet" ["nupkg-validator"; "build/output"] } }
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

The package depends on [`Proc.Fs`](https://www.nuget.org/packages/Proc.Fs), so `open Proc.Fs` gives you `exec { }` without another reference.

---

## Targets: `target [deps] { ... }`

`target [Clean] { ... }` declares a target that runs after `Clean`. The block is plain F#: `exec { }` calls, `if`, `match`, loops, `use`. It is deferred, so nothing in it runs until the target does. `-s` / `--single-target` skips the dependencies.

A failing `exec { }` fails the target, and `RunAsync` returns `1`.

## Commands: `command { ... }`

```fsharp
| Release -> command {
      requires [PristineCheck; Test { Filter = None }]   // gates, skipped under -s
      composes [Pkg Generate; Pkg Validate] }            // the work, always runs
```

## Descriptions from doc comments

A target's description is the `///` doc comment on its DU case. The comment is read from the XML documentation file, so the build project needs `<GenerateDocumentationFile>true</GenerateDocumentationFile>`. A `.fsx` script produces no documentation file. In a script, pass a description with `Make.target` (below), or the target shows its name.

## Namespaces via nested DUs

A case whose payload is another DU becomes a CLI namespace: `Pkg of PkgTarget` gives `pkg generate` and `pkg validate`, and `pkg --help` lists them. Two sub-DUs may share case names, such as `Validate`; qualify the ambiguous one in `Bind` (`| Schema SchemaTarget.Validate ->`).

## Per-target typed arguments

A case with a record payload binds the record's fields from CLI flags at execution time: `Test of TestOptions` with `{ Filter: string option }` takes `--filter`. A dependency can name any value of the case: `Test { Filter = Some "Unit" }` resolves to the `test` target.

## Global options

```fsharp
let verbose = app.Flag("--verbose", short = "-v", desc = "Enable verbose output")
let token   = app.Option<string option>("--token", desc = "API token", defaultValue = None)
```

Read them in target blocks through `.Value`. Help shows each option's type, such as `--config <string>`, and `--token <string?>` for an option type. A value that does not parse, or a missing value, exits `2` before any target runs. Enum options bind by name, ignoring case.

## The explicit functions

`target` and `command` are shorthand for these, which also take a description and give the body an `FsContext`:

```fsharp
Make.target  (deps: 'T list) (desc: string) (body: FsContext -> unit)
Make.command (requires: 'T list) (composes: 'T list)
Make.composer (requires: 'T list) (composes: 'T list) (body: FsContext -> unit)   // runs body after composes
failBuild "message"                                                               // abort with exit code 1
```

## Installation

```xml
<PackageReference Include="Nullean.Make.Fs" Version="0.23.0" />
```

Requires .NET 11. The package pulls in `Nullean.Make` (the underlying C# engine) and `Proc.Fs` transitively.
