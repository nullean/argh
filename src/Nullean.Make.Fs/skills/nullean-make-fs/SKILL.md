---
name: nullean-make-fs
description: >-
  Use when writing or changing an F# build project with Nullean.Make.Fs. Targets are DU cases bound with `target
  [deps] { sh $"..." }` Proc.Fs shell blocks and `command { requires; composes }`, with typed flags and generated
  --help.
---

# Nullean.Make.Fs

F# targets for Nullean.Make: each target is a case of a discriminated union, and a case you do not handle in `Bind`
is a compiler warning (FS0025). Target bodies are [Proc.Fs](https://www.nuget.org/packages/Proc.Fs) `shell { }`
blocks, which the package brings in.

## The calls you need

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
    /// Delete build output
    | Clean
    /// Build the solution
    | Build
    /// Build, then run the tests
    | Test of TestOptions
    | Pkg of PkgTarget
    /// Test, then pack and validate
    | Release

[<EntryPoint>]
let main argv =
    let app = MakeApp<Target>("build", None)
    let config = app.Option<string>("--config", desc = "Build configuration", defaultValue = "Release")

    app.Bind <| function
        | Clean -> target [] { sh $"dotnet clean" }
        | Build -> target [Clean] {
              let! sha = text $"git rev-parse HEAD"          // standard out, trimmed
              sh $"dotnet build -c {config.Value} -p:SourceRevisionId={sha}" }
        | Test o -> target [Build] {
              match o.Filter with
              | Some f -> sh $"dotnet test --filter {f}"     // {f} is one argument, whatever it contains
              | None -> sh $"dotnet test" }
        | Pkg Generate -> target [] { sh $"dotnet pack -c Release -o build/output" }
        | Pkg Validate -> target [] {
              for nupkg in System.IO.Directory.GetFiles("build/output", "*.nupkg") do
                  sh $"dotnet nupkg-validator validate {nupkg}" }
        | Release -> command {
              requires [Test { Filter = None }]
              composes [Pkg Generate; Pkg Validate] }

    app.RunAsync(argv).GetAwaiter().GetResult()
```

- `build --help`, `build test --filter Unit --config Debug`, `build pkg validate`, `build release -s`.
- `target [deps] { }`: dependencies run first; `-s` skips them. Inside: `sh` runs a command and fails the target on a
  non-zero exit; `let! s = text $"..."`, `let! ls = lines $"..."`, `let! code = exitCode $"..."` and
  `let! ok = succeeds $"..."` capture; `use! _ = inDir "src"` and `use! _ = withEnv [...]` scope the commands after
  them.
- `command { requires [...]; composes [...] }`: `requires` are gates skipped by `-s`; `composes` always run.
  `executes (shell { ... })` adds a body that runs last.
- A record payload is the target's flags (`Test of TestOptions` takes `--filter`). A DU payload is a namespace
  (`Pkg of PkgTarget` gives `pkg generate`).
- Global options: `app.Flag("--verbose", short = "-v", desc = "...")`, `app.Option<'T>(...)`, read with `.Value`
  inside a block.
- `<run> __wrappers sync --run "dotnet run --project build --"` writes mise, make and just includes with one task per
  target under `.make/`. It prints the include lines and mise hooks that regenerate them. `__wrappers check` exits `1`
  when one is stale. Do not hand-write wrapper tasks per target.

## Gotchas

- Descriptions are the `///` comments on the cases, read from the XML documentation file: the project needs
  `<GenerateDocumentationFile>true</GenerateDocumentationFile>`. A `.fsx` script has none; use
  `Make.target deps "description" body` there.
- `sh $"..."` splits only the literal text; each `{value}` stays one argument. Do not build a command string with
  `sprintf` and pass it in as one value: it becomes a single argument. Use an `if`, or a list spliced as `{args}`.
- Code in the `match` arm outside the block runs when the graph is read, for every case. Put the work inside
  `target [...] { }`.
- `RunAsync` returns the exit code (`1` when a target failed, `2` for a bad option); return it from `main`.
- Two sub-DUs that share a case name (`Validate`) need the qualified name in `Bind`: `Schema SchemaTarget.Validate`.

Docs: https://argh.nullean.net
