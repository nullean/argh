---
title: Arguments
---

# Arguments (positional)

Mark a parameter with `[Argument]` to make it positional. Indices must start at `0` and be consecutive.

```csharp
public static Task<int> Deploy([Argument] string environment) { … }
// myapp deploy production
```

## Variadic positionals

Combine `[Argument]` with a `T[]` type (or `params T[]`) to collect all remaining tokens into an array. The variadic argument must be the last positional. Because C# requires `params` to be the last method parameter, a variadic positional can appear after flags:

```csharp
// All remaining tokens become the array — zero items is valid.
public static void Copy([Argument] string dest, [Argument] params string[] files) { … }
// myapp copy ./out/ a.txt b.txt "path with spaces/c.txt"
// → dest="./out/", files=["a.txt", "b.txt", "path with spaces/c.txt"]

// Flags can appear before or after variadic tokens on the command line.
public static void Archive([Argument] params string[] files, bool verbose = false) { … }
// myapp archive a.zip b.zip --verbose   (verbose parsed as flag, the rest as files)

// Mixed: scalar positional first, then flags, then variadic (params must be last in C#).
public static void Tag([Argument] string target, bool force, [Argument] params string[] tags) { … }
// myapp tag main-branch --force tag1 tag2 tag3
```

## Help output

Variadic positionals appear as `<files...>` / `[<files...>]` in `--help` and include a `[variadic]` annotation.

## Count validation

Apply `[MinLength(n)]` or `[MaxLength(n)]` to validate the item count:

```csharp
public static void Archive([Argument][MinLength(1)][MaxLength(20)] string[] files) { … }
// --help: <files...>   [variadic] [count: 1–20]
```

## Ordering rules

- All `[Argument]` positionals must precede flags in the parameter list (by convention)
- Variadic positionals must be the last positional
- `params` must be the last method parameter (C# requirement)
- Flags can appear before or after positional tokens on the actual command line
