# Source-generated

Nullean.Argh uses a Roslyn incremental source generator that runs at build time. It analyzes your `Map`, `MapNamespace`, `UseGlobalOptions`, and `UseMiddleware` calls, then emits all parsing, dispatch, and help code directly into your assembly.

## What gets generated

The generator produces three files in your compilation:

- **`ArghGenerated.g.cs`** - dispatch switch tree, option parsers, help printers, completion tables, schema factory, module initializer
- **`ArghTypeBindingExtensions.g.cs`** - static extension methods for DTO binding (`[AsParameters]`)
- **`ArghNamespaceSegmentInitializer.g.cs`** - module initializer for namespace segment registration

## Why source generation

:::{tip}
You can read, step through, and debug the generated code like any other C# in your project.
:::

Traditional CLI frameworks use reflection at runtime to discover commands and bind arguments. This has costs:

- Startup time to scan assemblies
- Incompatible with Native AOT and trimming
- Errors surface at runtime, not build time
- No way to inspect the generated dispatch logic

With source generation, all of that shifts to compile time:

- Zero startup cost from CLI plumbing
- Fully AOT-compatible, no trimmer warnings
- Diagnostic errors at build time (AGH0001-AGH0022)
- Generated code is plain C# you can read in your IDE

## No runtime dependencies

The generated code targets `netstandard2.0` and uses no `System.Linq`, no dynamic dispatch, and no reflection. Lambda handlers (`UseMiddleware` with inline delegates) are the only exception and emit a warning (`AGH0006`).

## Incremental pipeline

The generator uses Roslyn's incremental pipeline for performance:

1. **Filter** - identifies relevant invocations by receiver namespace
2. **Analyze** - produces symbol-free `AnalyzedInvocation` records that Roslyn can cache per invocation
3. **Build** - assembles an `AppEmitModel` tree from cached records
4. **Emit** - writes the three generated files

Changes to one command only re-analyze that invocation. The rest is served from cache.

## Viewing generated code

In most IDEs, you can navigate to the generated files under Dependencies > Analyzers > Nullean.Argh.CliParserGenerator. You can also find them in:

```
.artifacts/obj/<YourProject>/<config>/Nullean.Argh.Generator/Nullean.Argh.CliParserGenerator/
```
