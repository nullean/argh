# Nullean.Make

Typed build targets for .NET, with [Argh](https://github.com/nullean/argh)-quality help. Your targets are the cases of a C# `union`; each one runs commands, and dependencies run first.

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

`Bind` runs twice per case: once when the app reads the graph, with each parameter at its default (`default` when it has none, such as `Deploy(string Environment)`), and once when the target runs, with the parsed flags. Keep side effects inside the target body.

The package's source generator finds each `new MakeApp<TUnion>(...)` and records the union's cases at build time, so the app reads no union by reflection. A `MakeApp<T>` created only through a type parameter has no case table and exits `1` with an error.

## Commands

`app.Command(requires, composes)` sequences other targets. `requires` are gates, such as tests, and `-s` skips them. `composes` always run, in order. An optional third argument is a body that runs last.

## Running commands

`using static Nullean.Make.Shell;` brings in `Exec`:

- `Exec("dotnet build -c Release")` splits on whitespace. Double quotes group an argument that contains spaces (`"My App.csproj"`), and `\"` is a literal quote.
- `Exec("dotnet", "build", "-c", config)` passes each argument as is. Use it for arguments built in code.

A command shares the console. A non-zero exit code fails the target, and `RunAsync` returns `1`.

## Descriptions

A target's description is the `<summary>` of its case type's XML doc comment. The comment is read from the XML documentation file, so set `GenerateDocumentationFile=true`: `#:property GenerateDocumentationFile=true` in a file-based app, or `<GenerateDocumentationFile>` in a project. `.Description("...")` overrides it.

## Global options

`app.Flag(...)` and `app.Option<T>(...)` declare options that every target can read through `.Value`. A value that does not parse, or a missing value, exits `2` before any target runs.

## Namespaces

A case that is itself a union becomes a namespace: `union Db(Migrate, Seed)` inside `union Target(..., Db)` gives `db migrate` and `db seed`, and `db --help` lists them. Case type names must be unique across the whole union, including nested unions.

## Class-based targets

`MakeApp.Execute<TBuild>(args)` discovers targets from a `MakeBuild` class instead: `Target` properties, `[Flag]` and `[GlobalOption]` properties, and nested `Namespace` classes. [`build.cs`](https://github.com/nullean/argh/blob/main/build.cs) in this repository is a complete example.

For F#, see [`Nullean.Make.Fs`](https://www.nuget.org/packages/Nullean.Make.Fs).
