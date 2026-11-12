# Nullean.Make

![Nullean.Argh](https://raw.githubusercontent.com/nullean/argh/main/docs/images/png/argh-lockup-348x192.png)

Typed build targets for .NET, with [Argh](https://github.com/nullean/argh)-quality help. Your targets are the cases of a C# `union`; each one runs commands, and dependencies run first.

[![NuGet](https://img.shields.io/nuget/v/Nullean.Make?style=flat-square)](https://www.nuget.org/packages/Nullean.Make)

## Install

```sh
dotnet add package Nullean.Make
```

Or, in a file-based app such as `build.cs`, `#:package Nullean.Make@<version>` as below. It requires .NET 11.

## Quick start

```csharp
#!/usr/bin/env -S dotnet run --
#:package Nullean.Make@0.24.0
#:property GenerateDocumentationFile=true
#:property NoWarn=CS1591
using Nullean.Make;
using static Nullean.Make.Shell;

var app = new MakeApp<Target>("build");
var config = app.Option<string>("--config", "Build configuration", defaultValue: "Release");

app.Bind(t => t.Value switch
{
    Clean     => app.Target(() => Exec("dotnet clean")),
    Build     => app.Target([new Clean()], () => Exec($"dotnet build -c {config.Value}")),
    Test test => app.Target([new Build()], () =>
    {
        if (test.Filter is { } filter) Exec($"dotnet test -c {config.Value} --filter:{filter}");
        else Exec($"dotnet test -c {config.Value}");
    }),
    Pack      => app.Target(() => Exec("dotnet pack -c Release -o build/output")),
    Release   => app.Command([new Test()], [new Pack()]),
    _ => throw new InvalidOperationException($"unmapped target {t.Value}")
});

return await app.RunAsync(args);

/// <summary>Clean known output locations</summary>
record Clean;
/// <summary>Run build</summary>
record Build;
/// <summary>Run build then tests</summary>
record Test(string? Filter = null);
/// <summary>Pack the NuGet packages</summary>
record Pack;
/// <summary>Test, then pack</summary>
record Release;

union Target(Clean, Build, Test, Pack, Release);
```

```
./build.cs --help
./build.cs test --filter Unit --config Debug
./build.cs release
./build.cs release -s        # skip the tests, run only pack
```

```
  Commands:  (pipeline entry points — compose and sequence targets)
  release    Test, then pack

  Targets:  (atomic steps — can also be run directly)
  clean    Clean known output locations
  build    Run build  (depends on: clean)
  test     Run build then tests  (depends on: build)
  pack     Pack the NuGet packages
```

## Targets

`app.Target(body)` and `app.Target(dependsOn, body)` take a plain lambda, so a body can branch, loop and read the case's fields. A `Func<Task>` body works too. Dependencies run first, once each, and `-s` / `--single-target` skips them.

A case record's primary-constructor parameters become the target's flags: `Test(string? Filter = null)` takes `--filter`.

A dependency can name a value: `app.Target([new Test("Unit")], ...)` runs `test` with `Filter = "Unit"`. Two
dependencies with equal values run the target once, and with different values once each. Only the target named on the
command line binds the command line's flags.

`Bind` runs twice per case: once when the app reads the graph, with each parameter at its default (`default` when it has none, such as `Deploy(string Environment)`), and once when the target runs, with the parsed flags. Keep side effects inside the target body.

The package's source generator finds each `new MakeApp<TUnion>(...)` and records the union's cases at build time, so the app reads no union by reflection. A `MakeApp<T>` created only through a type parameter has no case table and exits `1` with an error.

## Commands

`app.Command(requires, composes)` sequences other targets. `requires` are gates, such as tests, and `-s` skips them. `composes` always run, in order. An optional third argument is a body that runs last.

`app.Command()` builds the same in stages. `.Parallel(...)` adds a stage whose targets run side by side; the stages
run in the order they are declared:

```csharp
Release => app.Command().Requires(new Test()).Parallel(new PackNuget(), new DocsBuild()).Composes(new Publish()),
```

`-j` / `--jobs` caps how many targets run at once (default: one per processor). The first failure cancels the targets
still running; `-k` / `--keep-going` lets the ones that do not depend on it finish. Output of targets running side by
side carries their names, or `--output grouped` writes each target's lines together. See
[Running targets side by side](https://argh.nullean.net/make/parallel).

## Conditions, ordering and cleanup

A target builder takes more than dependencies:

```csharp
StartDb => app.Target(() => Exec("docker compose up -d db")).Cleanup(new StopDb()),  // runs at the end, even after a failure
Package => app.Target(() => Exec("dotnet pack")).After(new Lint()),                   // after lint when both run; does not add it
Publish => app.Target(() => Exec("dotnet nuget push"))
    .OnlyWhen(() => !dryRun.Value)        // skip the body; the dependencies still run
    .NeedsEnv("NUGET_API_KEY")            // checked before anything starts
    .NeedsTool("docker"),
```

See [Conditions, ordering and cleanup](https://argh.nullean.net/make/modifiers).

## Running commands

`using static Nullean.Make.Shell;` brings in `Exec`:

- `Exec("dotnet build -c Release")` splits on whitespace. Double quotes group an argument that contains spaces (`"My App.csproj"`), and `\"` is a literal quote.
- `Exec("dotnet", "build", "-c", config)` passes each argument as is. Use it for arguments built in code.

A command shares the console. A non-zero exit code fails the target, and `RunAsync` returns `1`.

## Descriptions

A target's description is the summary of its case type's XML doc comment, such as `Run build` above. The comment is read from the XML documentation file, so set `GenerateDocumentationFile=true`: `#:property GenerateDocumentationFile=true` in a file-based app, or `<GenerateDocumentationFile>` in a project. `.Description("...")` overrides it.

## Global options

`app.Flag(...)` and `app.Option<T>(...)` declare options that every target can read through `.Value`. A value that does not parse, or a missing value, exits `2` before any target runs.

## Namespaces

A case that is itself a union becomes a namespace: `union Db(Migrate, Seed)` inside `union Target(..., Db)` gives `db migrate` and `db seed`, and `db --help` lists them. Case type names must be unique across the whole union, including nested unions.

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

`__wrappers sync` writes one include file per wrapper from the targets, so `mise run test`, `make test` and
`just test` call the script without one hand-written task per target:

```
./build.cs __wrappers sync
wrote .make/mise-tasks.toml (5 targets)
wrote .make/make.mk (5 targets)
wrote .make/make.just (5 targets)
```

Pull each one in once:

| File | Line to add | Arguments |
|---|---|---|
| `mise.toml` | `[task_config]` `includes = [".make/mise-tasks.toml"]` | `mise run test --filter Unit` |
| `Makefile` | `-include .make/make.mk` | `make test ARGS="--filter Unit"` |
| `justfile` | `import? '.make/make.just'` | `just test --filter Unit` |

A namespaced target is `pkg:generate` in mise, `pkg/generate` in make and `pkg-generate` in just. The wrappers only
forward to the script, which still runs the dependencies, so a mise task has no `depends`. The mise tasks carry a
`usage` spec, so `mise` completes each target's flags, and `mise run test --help` prints the script's own help.

- `--run "<command>"` is what the wrappers call. A file-based app defaults to `dotnet run build.cs --`. A project
  needs it, e.g. `--run "dotnet run --project build --"`. Later syncs reuse the one recorded in the files.
- `--only mise,just` limits the files. A bare `sync` refreshes the files already in `--out-dir` (default `.make`).
- `--prefix make` names the tasks `make:test`, `make/test` and `make-test`. A task in `mise.toml` silently wins over
  an included task of the same name, so use a prefix when `mise.toml` already has `build` or `test` tasks.
- mise removes the first `--` after the task name, so `mise run test -- -- --x` passes `-- --x` to the script.
- `__wrappers check` exits `1` when a file is missing or out of date, for CI when the files are committed.
- `make.mk` keeps your Makefile's default goal. In a justfile, the first recipe is the default, so put yours above
  the `import?`.

Or let mise regenerate them and gitignore `.make/`. `sync` prints this when it creates a file:

```toml
[hooks]
# On every `mise install`, so a fresh clone and CI get the files.
postinstall = "dotnet run build.cs -- __wrappers sync --quiet"

# In a shell with `mise activate`, on the next prompt after build.cs changes.
[[watch_files]]
patterns = ["build.cs"]
run = "dotnet run build.cs -- __wrappers sync --quiet"
```

The `watch_files` hook compiles the script, so the first prompt after an edit waits for that.

The docs have a page per wrapper: [mise](https://argh.nullean.net/make/mise), [make](https://argh.nullean.net/make/makefile)
and [just](https://argh.nullean.net/make/just).

## Class-based targets

`MakeApp.Execute<TBuild>(args)` discovers targets from a `MakeBuild` class instead: `Target` properties, `[Flag]` and `[GlobalOption]` properties, and nested `Namespace` classes. A dependency on a `Target<T>` names its value with `With`: `.DependsOn(Test.With(new("Unit")))`. [`build.cs`](https://github.com/nullean/argh/blob/main/build.cs) in this repository is a complete example.

For F#, see [`Nullean.Make.Fs`](https://www.nuget.org/packages/Nullean.Make.Fs).

## Learn more

- Documentation: https://argh.nullean.net/make
- Source and issues: https://github.com/nullean/argh
