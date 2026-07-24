---
title: Flags
---

# Flags (named options)

Parameters without `[Argument]` become `--kebab-case` long flags. A `bool` flag defaults to `false`; pass `--flag` to set it.

```csharp
public static Task<int> Build(string outputDir, bool release = false) { … }
// myapp build --output-dir ./bin --release
```

## Naming convention

C# parameter names are automatically converted from camelCase to kebab-case:

- `outputDir` → `--output-dir`
- `dryRun` → `--dry-run`
- `verbose` → `--verbose`

## Long name override

By default the CLI long name is derived from the C# parameter name. Place a `--long-name` token before the description in an XML `<param>` tag to use a different primary name. Additional `--names` after the first become aliases. The derived name is dropped entirely once an explicit long name is specified.

```csharp
/// <summary>Tag one or more resources.</summary>
/// <param name="tags">-t, --tag, Tags to apply.</param>
public static void Tag(string[] tags) { … }
// --tag a --tag b   (NOT --tags — derived name is dropped)
// -t a              (short opt also works)
```

```csharp
/// <param name="outputDir">-o, --out, --output, Output directory.</param>
public static void Build(string outputDir) { … }
// --out ./bin        (primary)
// --output ./bin     (alias)
// -o ./bin           (short opt)
// --output-dir       (not recognized — derived name is dropped)
```

This also works on `[AsParameters]` properties, fields, and `[AsParameters]` primary-constructor parameters via their `<summary>` or `<param>` doc lines.

## Nullable bool — `--flag` / `--no-flag` pairs

A `bool?` flag generates **both** `--flag` (sets `true`) and `--no-flag` (sets `false`). Omitting either leaves the value `null`, letting you distinguish "not specified" from an explicit false. Help output shows `--flag / --no-flag` for nullable bools.

```csharp
public static Task<int> Deploy(string env, bool? dryRun = null) { … }
// myapp deploy staging               → dryRun is null
// myapp deploy staging --dry-run     → dryRun is true
// myapp deploy staging --no-dry-run  → dryRun is false
```

## Required vs optional

Parameters without a default value are required flags. Parameters with a default value are optional:

```csharp
public static Task<int> Deploy(
    string environment,           // required: --environment <value>
    bool dryRun = false,          // optional: --dry-run
    string? region = null)        // optional: --region <value>
{ … }
```

## Collections

Collection flags (`T[]`, `List<T>`) accept the flag multiple times, or use `[CollectionSyntax]` for comma-separated values:

```csharp
public static Task<int> Deploy(string[] targets, [CollectionSyntax(Separator = ",")] string[] tags) { … }
// Repeated:   myapp deploy --targets web --targets api
// Separator:  myapp deploy --targets web,api --tags blue,green
```
