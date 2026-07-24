---
title: Parameters
---

# Parameters and binding

Method parameters become CLI flags automatically. No attribute boilerplate for the common case.

## Overview

Nullean.Argh supports several parameter binding modes:

- **[Flags](flags.md)** - named options (`--output-dir ./bin`), derived from C# parameter names
- **[Arguments](arguments.md)** - positional parameters (`myapp deploy production`), marked with `[Argument]`
- **[DTO binding](dto-binding.md)** - expand records/classes into individual flags with `[AsParameters]`
- **[Custom parsers](custom-parsers.md)** - `IArgumentParser<T>` for types with no built-in support
- **[Validation](validation.md)** - DataAnnotations and filesystem path attributes

## Supported types

| Category | Types |
|----------|-------|
| Primitives | `string`, `int`, `long`, `double`, `float`, `decimal`, `bool`, `bool?` |
| System | `enum`, `FileInfo`, `DirectoryInfo`, `Uri` |
| Collections | `List<T>`, `T[]` |

### Collections

Collection flags accept the flag multiple times by default. Use `[CollectionSyntax(Separator=",")]` for comma-separated input instead. For variadic positionals, use `[Argument] T[]` or `[Argument] params T[]`.

::::tab-set

:::tab-item Repeated flag
:sync: repeated

```csharp
public static Task<int> Deploy(string[] targets) { … }
```

```
myapp deploy --targets web --targets api
```

:::

:::tab-item Comma-separated
:sync: separator

```csharp
public static Task<int> Deploy([CollectionSyntax(Separator = ",")] string[] tags) { … }
```

```
myapp deploy --tags blue,green
```

:::

::::

## How it works

The source generator reads your method signatures at build time and emits typed parsers for each parameter. Parameter names are converted from camelCase to kebab-case for CLI flags.

Parameters with default values are optional; those without are required.
