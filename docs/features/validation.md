# Validation

Annotate parameters (or `[AsParameters]` members) with standard `System.ComponentModel.DataAnnotations` attributes, optionally combined with Nullean.Argh filesystem attributes. The source generator reads the attributes at build time and emits inline validation checks. No reflection, no `Validator.ValidateObject` call, AOT-safe.

Constraint hints appear in `--help` after the description. Failures print to stderr and exit with code 2.

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
  --port <int>       [required] [range: 1-65535]
  --name <string>    [required] [length: 2-64]
  --env <string>     [required] [allowed: dev|staging|prod]
  --slug <string>    [required] [pattern: ^[a-z0-9\-]+$]
  --endpoint <uri>   [required] [schemes: https]
```

## DataAnnotations reference

:::{dropdown} Full attribute reference table
| Attribute | Applies to | Validates | Help token |
|-----------|------------|-----------|-----------|
| `[Range(min, max)]` | numeric | value within bounds | `[range: min-max]` |
| `[StringLength(max)]` | `string` | string length | `[max-length: n]` |
| `[StringLength(max, MinimumLength = min)]` | `string` | string length | `[length: min-max]` |
| `[MinLength(n)]` / `[MaxLength(n)]` | `string` | string length | `[min-length: n]` / `[max-length: n]` |
| `[MinLength(n)]` / `[MaxLength(n)]` | collection | item count | `[min-count: n]` / `[max-count: n]` |
| `[Length(min, max)]` (.NET 8) | `string` | string length | `[length: min-max]` |
| `[Length(min, max)]` (.NET 8) | collection | item count | `[count: min-max]` |
| `[RegularExpression(pattern)]` | `string` | regex match | `[pattern: …]` |
| `[AllowedValues(v1, v2, …)]` (.NET 8) | any | value in set | `[allowed: v1\|v2\|…]` |
| `[DeniedValues(v1, v2, …)]` (.NET 8) | any | value not in set | `[denied: v1\|v2\|…]` |
| `[EmailAddress]` | `string` | `user@host` shape | `[email]` |
| `[Url]` | `string` | absolute URL | `[url]` |
| `[Url]` | `Uri` | http or https | `[schemes: http\|https]` |
| `[FileExtensions(Extensions="json,yaml")]` | `FileInfo` (incl. collections) | file extension | `[extensions: json\|yaml]` |
| `[UriScheme("https")]` | `Uri` | URI scheme | `[schemes: https]` |
:::

## Collection validation

When `[MinLength]` / `[MaxLength]` / `[Length]` is applied to a collection parameter (`T[]`, `List<T>`, etc.), it validates the **number of items**, not string length:

```csharp
public static void Process([MaxLength(5)] List<string> files) { … } // max 5 --file flags

public static void Archive([Argument][MinLength(2)][MaxLength(10)] string[] files) { … } // 2-10 positional items
```

:::{note}
Enum parameters automatically show `[allowed: Member1|Member2]` in help. The enum type itself enforces the constraint with no extra attribute needed.
:::

## Filesystem path validation

These attributes apply to `FileInfo` / `DirectoryInfo` parameters (including on `[AsParameters]` members and `[Argument]` positionals). Incompatible combinations (such as `[Existing]` with `[NonExisting]`) are diagnosed at compile time, as is applying `[FileExtensions]` to a `DirectoryInfo`.

| Attribute | Applies to | Help token |
|-----------|------------|------------|
| `[Existing]` | `FileInfo` or `DirectoryInfo` | `[existing]` |
| `[NonExisting]` | `FileInfo` or `DirectoryInfo` | `[unused path]` |
| `[RejectSymbolicLinks]` | `FileInfo` or `DirectoryInfo` | `[no symlinks]` |
| `[ExpandUserProfile]` | `FileInfo` or `DirectoryInfo` | `[expand ~ profile]` |
| `[FileExtensions(Extensions="…")]` | `FileInfo` only | `[extensions: …]` |

:::{important}
`[RejectSymbolicLinks]` runs before existence checks. A symlink pointing to a real path still fails when symlink rejection is enabled.
:::

### Example

```csharp
public static Task<int> Lint(
    [Existing][FileExtensions(Extensions="json")][RejectSymbolicLinks] FileInfo manifest,
    [ExpandUserProfile][Existing] DirectoryInfo outDir)
{ … }
```

Failures produce stderr messages such as *file does not exist* or *path must not be a symbolic link* (exit code 2).

### Collections of FileInfo / DirectoryInfo

All five filesystem attributes also work on a collection of `FileInfo` / `DirectoryInfo` — `List<FileInfo>`, `FileInfo[]`, `DirectoryInfo[]`, etc. — including a variadic `[Argument]` collection (`FileInfo[]`/`params FileInfo[]`). Each attribute is applied **per item**, and every failing item is reported together in a single error, not just the first one encountered:

```csharp
public static void Ingest(
    [Existing][FileExtensions(Extensions = "json,yaml")] List<FileInfo> configs) { … }

public static void Archive(
    [Argument][Existing][FileExtensions(Extensions = "zip")] FileInfo[] files) { … }
```

```
$ myapp ingest --configs a.json --configs missing.txt --configs b.png
Error: --configs: /path/missing.txt: file does not exist.
Error: --configs: /path/b.png: extension must be one of: json, yaml.
Run 'myapp ingest --help' for usage.
```

`[MinLength]` / `[MaxLength]` / `[Length]` on the same collection continue to validate item *count* (see [Collection validation](#collection-validation) above) and combine with the per-item filesystem checks.

## Schema integration

Validations include JSON `kind` values in `__schema` output: `existing`, `nonExisting`, `rejectSymbolicLinks`, `expandUserProfile`.

Validation also runs through the `TryParseArgh` static extension emitted for `[AsParameters]` DTOs, so unit tests can assert constraints without spawning a subprocess.
