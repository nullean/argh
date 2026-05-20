# Nullean.Make — Top-level Field DSL (follow-up design notes)

The current C# `Nullean.Make` ships a property-dispatch DSL on a `MakeBuild` subclass — already implemented and working. This doc captures a follow-up design exploration: a top-level, field-based alternative that fits better inside a single-file `dotnet run build.cs` script.

---

## Pain points with the current property-dispatch style in a single-file script

```csharp
return await MakeApp.Execute<Build>(args);

class Build : MakeBuild
{
    public Target Clean => _ => _
        .Description("Delete build output")
        .Executes(() => { … });
}
```

- Requires an enclosing `class Build : MakeBuild { … }`.
- `_ => _` is ceremony — a lambda that accepts a builder, configures it, and returns it. Necessary for the property-dispatch model but reads as noise inside a top-level script.
- String names (in `Target<T>` only; property-dispatch derives names from property names) are fine, but other friction points remain.

---

## Core idea

**`Target` is a delegate type. `make.Add(delegate)` captures the variable name via `[CallerArgumentExpression]`.**

Two C# language features do all the work:

1. `delegate void Target()` (and `delegate void Target<T>(T opts)`) — same shape as `Action` / `Action<T>` but a distinct named type. Lets you write `Target Clean = () => Exec("dotnet", "clean");` as a plain variable assignment. No `_ => _`, no class, no string.

2. `[CallerArgumentExpression(nameof(body))]` on the registration method — `make.Add(Clean)` captures `"Clean"` from the source text, kebab-cases it to `clean`, and registers the node. Cross-references in `.DependsOn(Clean)` pass the delegate instance; the framework resolves it back via `Dictionary<Delegate, TargetNode>`.

| Phase | What happens | How |
|---|---|---|
| **Setup** | Assign a delegate body to a typed local | `Target Clean = () => Exec(…);` |
| **Composition** | Register, name, describe, wire deps | `make.Add(Clean).Description(…).DependsOn(…);` |
| **Run** | Discover the graph, dispatch | `return await make.RunAsync(args);` |

---

## Worked example

```csharp
#!/usr/bin/env dotnet run
#:project src/Nullean.Make

using Nullean.Make;
using static ProcNet.Proc;

var make = new MakeApp("argh-build", "Build pipeline for nullean/argh");

// Global options — closed over by target bodies directly.
var token        = make.Option<string?>("--token", description: "GitHub token");
var skipPristine = make.Flag("--clean-checkout", "-c", description: "Skip pristine-checkout guard");

// ── Setup: bodies as plain delegates ───────────────────────────────────────
Target Clean         = () => Exec("dotnet", "clean");
Target Build         = () => Exec("dotnet", "build", "-c", "Release");
Target PristineCheck = () => { /* git status --porcelain */ };
Target ReleaseNotes  = () => { /* … */ };
Target ApiChanges    = () => { /* … */ };

Target<TestOptions> Test = opts => Exec(
    "dotnet", "test", "-c", "Release",
    opts.Filter is null ? "" : $"--filter:{opts.Filter}");

// ── Composition: register + name (captured from variable) + wire deps ──────
make.Add(Clean).Description("Delete build output");
make.Add(Build).Description("dotnet build -c Release").DependsOn(Clean);
make.Add(Test).DependsOn(Build);
make.Add(PristineCheck)
    .Description("Verify pending changes")
    .OnlyWhen(() => !skipPristine.Value);
make.Add(ReleaseNotes).Description("Generate release-notes-<ver>.md");
make.Add(ApiChanges).Description("Generate breaking-changes-<pkg>.md");

// ── Namespaces ─────────────────────────────────────────────────────────────
Namespace Pkg = make.Namespace("pkg", "Package operations");

Target PkgGenerate = () => Exec("dotnet", "pack", "-c", "Release");
Target PkgValidate = () => { /* nupkg-validator loop */ };

// Factory form — preferred, captures name via [CallerArgumentExpression]:
Pkg.Add(PkgGenerate).Description("dotnet pack");
Pkg.Add(PkgValidate).Description("nupkg-validator on each .nupkg");

// Operator form — sugar, reads well for no-chain cases:
Namespace Schema = make.Namespace("schema", "Schema export operations");
Target SchemaUpdate   = () => { /* … */ };
Target SchemaValidate = () => { /* … */ };
Schema += SchemaUpdate;
Schema += SchemaValidate;

// Late description (when += was used and chaining isn't possible):
make[SchemaUpdate].Description("Regenerate schema/argh-cli-schema.json");
make[SchemaValidate].Description("Fail if schema out of date");

// ── Commands ───────────────────────────────────────────────────────────────
Command Release = () => { };   // pure composer — no trailing body
Command Publish = () => Console.WriteLine($"published at {DateTime.UtcNow:o}");

make.Add(Release)
    .Description("Verify gates → pack → notes → diff")
    .Requires(PristineCheck, Test)
    .Composes(PkgGenerate, PkgValidate, ReleaseNotes, ApiChanges);

make.Add(Publish)
    .Description("Release → create GitHub release")
    .Requires(Release);

return await make.RunAsync(args);

record TestOptions([Argument] string? Filter = null);
```

**What's gone vs. property-dispatch:**
- No `class Build : MakeBuild { … }`.
- No `_ => _` builder lambda.
- No nested `class Pkg : Namespace { … }` for namespacing.
- No `[GlobalOption]` / `[Flag]` attributes.
- No redundant string names — `Target Clean = …` is the only place `Clean` appears; `make.Add(Clean)` derives the CLI name via `[CallerArgumentExpression]`.

**What's kept:**
- Identity comes from the variable name (rename in IDE → propagates to `.DependsOn(…)` callsites automatically).
- Same `BuildGraph`, `DepGraphExecutor`, `ArgvParser`, `MakeHelpPrinter`, per-target DTO binding via Argh.Core.
- F# `Nullean.Make.Fs` unchanged — DU + match is already idiomatic, no `_ => _` pain there.

---

## Identity and naming

| Concern | Resolution |
|---|---|
| CLI name source | `[CallerArgumentExpression(nameof(body))]` on `make.Add(Target body, …)`. Caller writes `make.Add(Clean)` → captured text `"Clean"` → kebab-case `clean`. |
| Cross-reference identity | The delegate instance itself. `Dictionary<Delegate, TargetNode>` maps registered targets. `.DependsOn(Clean)` resolves at registration time. |
| Rename safety | Renaming `Clean` → `Tidy` is a one-step IDE refactor; propagates to all `.DependsOn(Clean)` callsites. CLI route changes from `clean` to `tidy` automatically. |
| Atypical call shapes | `make.Add((Target)X)` → captured text is `"(Target)X"` (not a valid identifier). Framework validates and throws `MakeException` with a useful message at registration. |
| Duplicate registration | Same delegate instance registered twice → throws at registration. Two separate lambdas with identical bodies are different instances (compiler caches per source location). |

### `+=` operator and name capture

`Pkg += SchemaUpdate` compiles to `Pkg = Pkg + SchemaUpdate`. Operator methods CAN carry `[CallerArgumentExpression]` (it's just a static method under the hood) — this needs to be verified experimentally. If the C# compiler inserts the RHS source text `"SchemaUpdate"` at the callsite, `+=` works. If not, `Pkg.Add(target)` is the only name-capturing path and `+=` is dropped.

### Late composition — indexer

```csharp
make[SchemaUpdate].Description("…");
```

`make[delegate]` returns the `ITargetBuilder` for an already-registered node. Useful when `+=` was used and the chained `.Description(…)` call isn't available inline.

---

## Internals — what would need to change

Everything below the registration point is **reused as-is**: `BuildGraph`, `TargetNode`, `GraphValidator`, `DepGraphExecutor`, `ArgvParser`, `DtoBinder`, `MakeHelpPrinter`.

### Changed types

| File | Change |
|---|---|
| `Target.cs` | Currently `delegate ITargetBuilder Target(ITargetBuilder b)`. Change to `delegate void Target()`. |
| `TargetOfT.cs` | Currently `delegate ITargetBuilder<T> Target<T>(ITargetBuilder<T> b)`. Change to `delegate void Target<T>(T opts)`. |
| `Command.cs` / `CommandOfT.cs` | Same shape change. |
| `ITargetRef.cs` / `TargetRef` | **Delete.** The delegate itself is the ref. |
| `MakeApp.cs` | Currently a static class. Becomes an **instantiable class**: ctor `(name, description?)`, methods `Option<T>` / `Flag` / `Namespace(name)` / `Add(Target)` / `Add<T>(Target<T>)` / `Add(Command)` / `Add<T>(Command<T>)` / indexer `[Delegate]` / `RunAsync(args)`. |
| `Namespace.cs` | Currently an empty abstract marker. Becomes a **concrete class** with `.Add(…)` factory (mirrors `MakeApp.Add`, prefixes the route) and `operator +` sugar. |
| `Discovery/TargetBuilderImpl.cs` | Stays as the chainable builder returned from `Add(…)`. `MethodInfo`-keyed lookup replaced by delegate-keyed lookup. |

### Registration — push-based, not reflection-scan

`BuildScanner.cs` is bypassed for this surface. `MakeApp.Add(delegate)` and `Namespace.Add(delegate)` push nodes directly into the `BuildGraph`. The reflection scanner stays available if property-dispatch is kept as a parallel surface.

---

## Things to verify before implementing

1. **`[CallerArgumentExpression]` through operator overloads.** Verify `operator +(Namespace, Target t, [CallerArgumentExpression(nameof(t))] string? expr)` actually captures `"SchemaUpdate"` from `Schema += SchemaUpdate`. If it doesn't, drop `+=` and keep only `Pkg.Add(target)`.

2. **Delegate equality semantics in practice.** Write a small test: two separate `Target x = () => …;` lambdas at different source locations should produce non-equal delegates even with identical bodies. Confirm they key separately in `Dictionary<Delegate, …>`.

3. **Overload resolution between `Target` and `Command`.** Both are `delegate void()`. C# should treat them as distinct types (different `delegate` declarations), so `make.Add(Target t)` and `make.Add(Command c)` resolve unambiguously. Confirm experimentally.

---

## Open question

**Keep property-dispatch as a parallel surface, or drop it?**

Coexistence costs ~250 LoC of scanner code and the static `MakeApp.Execute<TBuild>` entry point, but gives class-based / testable pipeline authors a home. Dropping shrinks the public surface to one shape.

Decide after the new surface is fully implemented and the worked example reads end-to-end.
