# Argh

Source-generated, AOT-safe CLI framework for .NET (`Nullean.Argh`, `Nullean.Argh.Hosting`), plus `Nullean.Make` and
`Nullean.Make.Fs`, a typed build target DSL built on it. The docs site (`docs/`, argh.nullean.net) is a docs-builder
site published by `.github/workflows/gh-pages.yml`.

## Commands

mise installs the SDK pinned in `global.json`, plus the net10.0 runtime the `release-notes` tool runs on. Run
everything through it:

```bash
mise run build    # build the solution
mise run test     # build, then run the tests
mise run pack     # build, pack the NuGet packages, validate them, report public API changes
mise run ci       # exactly what CI's build job runs: test, check the schema, pack, validate, diff the public API
mise run docs     # build the docs as the deploy does (theme, landing page) and serve them; `-- --noserve` builds only
mise run check    # workflow security checks (zizmor)
mise run clean    # delete build output
```

`mise tasks ls` lists them all. The tasks call the F# build in `build/scripts` (Argu + Bullseye); run a target
directly with `dotnet run --project build/scripts -- <target>`, and `-s true` to skip its dependencies. Packages and
API reports go to `build/output`. `dotnet run --project build/scripts -- updateschema` regenerates
`schema/argh-cli-schema.json`, which `mise run ci` fails on when it is out of date.

`nupkg-validator` stays last in `dotnet-tools.json`: SDK 11's `dotnet tool restore` fails on a clean machine when
that RID-specific tool package comes before `assembly-differ`.

`build.cs` is the same pipeline written with `Nullean.Make` (C#), kept as its complete example.

For a narrower loop, `dotnet test` works directly:

```bash
dotnet test tests/Nullean.Argh.IntegrationTests/ -c Release
dotnet test tests/Nullean.Argh.IntegrationTests/ -c Release --filter "FullyQualifiedName~HelpContent"
```

## Architecture

**Five packages, two entry points for consumers:**
- `Nullean.Argh` — console apps (metapackage pulling Core + Interfaces)
- `Nullean.Argh.Hosting` — DI/hosted-service integration (`Microsoft.Extensions.*`)

**The packages:**

| Project | Role |
|---|---|
| `Nullean.Argh.Interfaces` | Zero-dependency contracts: attributes, `IArghBuilder`, `IArghNamespaceBuilder`, `IArgumentParser<T>`, middleware interfaces |
| `Nullean.Argh.Core` | Runtime (`ArghRuntime`, `ArghParser`, `ArghApp`), help rendering, schema types. Also embeds the generator. |
| `Nullean.Argh.Generator` | Roslyn `IIncrementalGenerator` — runs at build time, emits all parsing/dispatch/help code |
| `Nullean.Argh` | Metapackage (no code) |
| `Nullean.Argh.Hosting` | `AddArgh()` extension, runs CLI as `IHostedService`, wires `CancellationToken` to host lifetime |

## How the Generator Works

`CliParserGenerator` (in `src/Nullean.Argh.Generator/`) scans the user's code for invocations of `Add`, `AddNamespace`, `AddRootCommand`, `AddNamespaceRootCommand`, `GlobalOptions`, `CommandNamespaceOptions`, and `UseMiddleware` on `IArghBuilder`/`ArghApp`. It:

1. **Filters** invocations by receiver namespace (`Nullean.Argh`) in the pipeline `CreateSyntaxProvider` transform before `Collect()`.
2. **Analyzes** each invocation independently in the Select() step, producing symbol-free `AnalyzedInvocation` records that Roslyn can cache per invocation.
3. **Builds** an `AppEmitModel` / `RegistryNode` tree from the cached records in `RegisterSourceOutput`.
4. **Emits** into three generated files:
   - `ArghGenerated.g.cs` — dispatch switch tree, option parsers, help printers, completion tables, schema factory, module initializer
   - `ArghTypeBindingExtensions.g.cs` — C# 14 static extension methods for DTO binding (`[AsParameters]`)
   - `ArghNamespaceSegmentInitializer.g.cs` — module initializer for argless namespace segment registration

The generator uses a single **hierarchical** emit path; flat CLIs (no namespaces, no global options) are handled as the depth-0 degenerate case.

**Diagnostics** `AGH0001`–`AGH0022` are defined at the top of `CliParserGenerator.cs`. Errors halt code generation; warnings allow it to continue.

**Key internal types** (all inside the partial `CliParserGenerator` class):
- `ParameterModel` — fully symbol-free; one instance per CLI flag/positional/injected param
- `CommandModel` — fully symbol-free; one per handler method; flows through the incremental pipeline
- `AnalyzedInvocation` subtypes (`AIAddCommand`, `AIAddNamespace`, etc.) — symbol-free pipeline records; all fields are strings, value types, or `ImmutableArray`s; no `ISymbol` or `Location` references
- `RegistryNode` / `AppEmitModel` — mutable tree built during `TryBuildAppEmitModel` from cached records

## Testing

**`Nullean.Argh.Tests.CliHost`** is an executable (not a test project) that the integration tests spawn as a subprocess. It is the test fixture host.

**`Nullean.Argh.IntegrationTests`** invokes `CliHostRunner.Run(...)`, capturing stdout/stderr and exit codes. Tests are organized under `Help/`, `Commands/`, `Binding/`, `Completions/`, `Middleware/`, `ParseErrors/`.

**`Nullean.Argh.Tests`** runs unit tests against the generated code directly (xunit + FluentAssertions). Test fixtures live in `tests/Nullean.Argh.Tests/Fixtures/`.

The generator itself has minimal direct unit tests — coverage is primarily through the integration test suite.

## Important Constraints

- Generated code targets `netstandard2.0` (no `System.Linq`, no pattern features beyond what that TFM allows) except the static extension methods in `ArghTypeBindingExtensions.g.cs` which require **C# 14** (`static extension` members).
- The project is **AOT-safe**: no reflection in generated code, no dynamic dispatch. Lambda handlers (`UseMiddleware` inline delegates) are the one exception and emit a warning (`AGH0006`).
- `SymbolEqualityComparer.Default` must be used for all `ISymbol` comparisons — never `==`.
- The build uses `<UseArtifactsOutput>true</UseArtifactsOutput>` (in `Directory.Build.props`): all outputs go to `.artifacts/bin/<Project>/` and `.artifacts/obj/`. Do not reference `bin/` or `obj/` paths.
