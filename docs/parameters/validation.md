---
title: Validation
---

# Validation

Annotate parameters (or `[AsParameters]` members) with standard **`System.ComponentModel.DataAnnotations`** attributes, optionally combined with **Nullean.Argh** filesystem attributes. The source generator reads the attributes at build time and emits inline validation checks — no reflection, no `Validator.ValidateObject` call, AOT-safe.

Constraint hints appear in `--help` after the description; failures print to stderr and exit with code 2.

## Example

```csharp
public static void Deploy(
    [Range(1, 65535)]                          int port,
    [StringLength(64, MinimumLength = 2)]      string name,
    [AllowedValues("dev", "staging", "prod")]  string env,
    [RegularExpression(@"^[a-z0-9\-]+$")]      string slug,
    [UriScheme("https")]                       Uri endpoint)
{ … }
```

```
$ myapp deploy --port 99999
Error: --port: value must be between 1 and 65535.
Run 'myapp deploy --help' for usage.

$ myapp deploy --help
Options:
  --port <int>       [required] [range: 1–65535]
  --name <string>    [required] [length: 2–64]
  --env <string>     [required] [allowed: dev|staging|prod]
  --slug <string>    [required] [pattern: ^[a-z0-9\-]+$]
  --endpoint <uri>   [required] [schemes: https]
```

## DataAnnotations reference

| Attribute | Applies to | Validates | Help token |
|-----------|------------|-----------|-----------|
| `[Range(min, max)]` | numeric | numeric value is within bounds | `[range: min–max]` |
| `[StringLength(max)]` / `[StringLength(max, MinimumLength = min)]` | `string` | string length | `[max-length: n]` / `[length: min–max]` |
| `[MinLength(n)]` / `[MaxLength(n)]` on `string` | `string` | string length | `[min-length: n]` / `[max-length: n]` |
| `[MinLength(n)]` / `[MaxLength(n)]` on a collection | `T[]`, `List<T>`, etc. | item count | `[min-count: n]` / `[max-count: n]` |
| `[Length(min, max)]` (.NET 8) on `string` | `string` | string length range | `[length: min–max]` |
| `[Length(min, max)]` (.NET 8) on a collection | `T[]`, `List<T>`, etc. | item count range | `[count: min–max]` |
| `[RegularExpression(pattern)]` | `string` | value matches regex | `[pattern: …]` |
| `[AllowedValues(v1, v2, …)]` (.NET 8) | any | value is in the set | `[allowed: v1\|v2\|…]` |
| `[DeniedValues(v1, v2, …)]` (.NET 8) | any | value is not in the set | `[denied: v1\|v2\|…]` |
| `[EmailAddress]` | `string` | basic `user@host` shape | `[email]` |
| `[Url]` on `string` | `string` | absolute URL (http/https/ftp) | `[url]` |
| `[Url]` on `Uri` | `Uri` | scheme is http or https | `[schemes: http\|https]` |
| `[FileExtensions(Extensions="json,yaml")]` | `FileInfo` | `FileInfo` extension | `[extensions: json\|yaml]` |
| `[UriScheme("https")]` *(Argh-native)* | `Uri` | `Uri` scheme is in the list | `[schemes: https]` |

## Collection validation

When `[MinLength]` / `[MaxLength]` / `[Length]` is applied to a **collection** parameter (`T[]`, `List<T>`, `IReadOnlySet<T>`, etc.), it validates the **number of items**, not the length of a string:

```csharp
// Flag: must receive --file at least once, at most five times
public static void Process([MaxLength(5)] List<string> files) { … }

// Variadic positional: between 2 and 10 items required
public static void Archive([Argument][MinLength(2)][MaxLength(10)] string[] files) { … }
```

Enum parameters automatically show `[allowed: Member1|Member2]` in help — the enum type itself enforces the constraint, no extra attribute needed.

## Filesystem path validation

These attributes apply to **`FileInfo`** / **`FileInfo?`** or **`DirectoryInfo`** / **`DirectoryInfo?`** (including on `[AsParameters]` members). Incompatible combinations (such as `[Existing]` with `[NonExisting]`) are diagnosed at compile time.

| Attribute | Applies to | Validates | Help token |
|-----------|------------|-----------|------------|
| `[Existing]` | `FileInfo` / `FileInfo?` | `File.Exists` | `[existing]` |
| `[Existing]` | `DirectoryInfo` / `DirectoryInfo?` | `Directory.Exists` | `[existing]` |
| `[NonExisting]` | `FileInfo` / `FileInfo?` or `DirectoryInfo` / `DirectoryInfo?` | neither file nor directory exists | `[unused path]` |
| `[RejectSymbolicLinks]` | `FileInfo` / `FileInfo?` or `DirectoryInfo` / `DirectoryInfo?` | not a symlink or reparse point | `[no symlinks]` |
| `[ExpandUserProfile]` | `FileInfo` or `DirectoryInfo` | expands `~/` before construction, then `Path.GetFullPath` | `[expand ~ profile]` |

**`[RejectSymbolicLinks]`** runs before existence checks — a symlink to a real path still fails when symlink rejection is enabled.

### Example

```csharp
public static Task<int> Lint(
    [Existing][FileExtensions(Extensions="json")][RejectSymbolicLinks] FileInfo manifest,
    [ExpandUserProfile][Existing] DirectoryInfo outDir)
{ … }
```

Failures use stderr messages such as *file does not exist*, *directory does not exist*, *path already exists…*, or *path must not be a symbolic link or reparse point* (exit code 2).

## Schema integration

Validations include JSON `kind` values in `__schema` output such as `existing`, `nonExisting`, `rejectSymbolicLinks`, and `expandUserProfile`.

Validation also runs through the `TryParseArgh` static extension emitted for `[AsParameters]` DTOs, so unit tests can assert constraints without spawning a subprocess.
