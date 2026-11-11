---
name: nullean-make
description: >-
  Use when writing or changing a C# build script with Nullean.Make (often a `dotnet run build.cs` file). Targets are
  the cases of a C# union, with dependencies, pipeline commands, typed per-target flags and generated --help.
---

# Nullean.Make

A typed build target runner on top of Nullean.Argh: each target is a record, the records form a `union`, and `Bind`
maps each case to what it runs. For F#, use `Nullean.Make.Fs` (skill `nullean-make-fs`).

## The calls you need

```csharp
#!/usr/bin/env -S dotnet run --
#:package Nullean.Make@0.25.0
#:property GenerateDocumentationFile=true
#:property NoWarn=CS1591
using Nullean.Make;
using static Nullean.Make.Shell;

var app = new MakeApp<Target>("build");
var config = app.Option<string>("--config", "Build configuration", defaultValue: "Release");

app.Bind(t => t.Value switch
{
	Clean => app.Target(() => Exec("dotnet clean")),
	Build => app.Target([new Clean()], () => Exec("dotnet", "build", "-c", config.Value)),
	Test test => app.Target([new Build()], () =>
	{
		if (test.Filter is { } filter) Exec("dotnet", "test", "--filter", filter);
		else Exec("dotnet test");
	}),
	Pkg { Value: Generate } => app.Target(() => Exec("dotnet pack -c Release -o build/output")),
	Pkg { Value: Validate } => app.Target(() => Exec("dotnet nupkg-validator validate build/output")),
	Release => app.Command([new Test()], [new Pkg(new Generate()), new Pkg(new Validate())]),
	_ => throw new InvalidOperationException($"unmapped target {t.Value}")
});

return await app.RunAsync(args);

/// <summary>Delete build output</summary>
record Clean;
/// <summary>Build the solution</summary>
record Build;
/// <summary>Build, then run the tests</summary>
record Test(string? Filter = null);
/// <summary>Pack the packages</summary>
record Generate;
/// <summary>Validate the packages</summary>
record Validate;
/// <summary>Test, then pack and validate</summary>
record Release;

union Pkg(Generate, Validate);
union Target(Clean, Build, Test, Pkg, Release);
```

- `./build.cs --help`, `./build.cs test --filter Unit --config Debug`, `./build.cs pkg generate`,
  `./build.cs release -s` (`-s` skips dependencies and `requires`; `composes` still run).
- `app.Target(deps, body)`: dependencies run first, once each. `app.Command(requires, composes)` sequences other
  targets: `requires` are gates, `composes` the work, in order; an optional third argument runs last.
- A case record's constructor parameters are that target's flags: `Test(string? Filter = null)` takes `--filter`.
- A nested union is a namespace: `Pkg` gives `pkg generate` and `pkg validate`. Match it with
  `Pkg { Value: Generate }` and create it with `new Pkg(new Generate())`.
- A non-zero exit code from `Exec` fails the target, and `RunAsync` returns `1`. A bad or missing option value
  returns `2` before anything runs.

## Gotchas

- Return the exit code: `return await app.RunAsync(args);`. A bare `await` exits `0` even when a target failed.
- `Exec("...")` splits the string on whitespace (double quotes group). Pass values built in code as separate
  arguments, `Exec("dotnet", "build", "-c", config.Value)`, so a value with a space stays one argument.
- `Bind` runs once to read the graph (case fields at their defaults) and again when the target runs. Keep side effects
  inside the target body, not in the switch arm.
- Descriptions come from the `<summary>` on each case record and need `GenerateDocumentationFile`; without it help
  shows bare names.
- A case type name must be unique across the whole union, nested unions included. A nested case is not implicitly a
  `Target`: `new Generate()` where a `Target` is expected does not compile.

Docs: https://argh.nullean.net
