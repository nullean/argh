# Union parameters

A parameter typed as a C# 15 `union` lets the caller pick one case on the command line. Each case's primary-constructor parameters become flags of their own. Unions need .NET 11 and C# 15.

```csharp
public record class Json(bool Pretty = false, int Indent = 2);
public record class Table(bool Pretty = false);
public record class Csv;

public union Format(Table, Json, Csv);
```

Each case binds by its kebab-case type name: `Json` becomes `json`.

## Flag mode

By default a flag named after the parameter selects the case. Case flags take the case name as a prefix, so `Json.Pretty` and `Table.Pretty` do not collide.

```csharp
public static void Convert(Format format) { … }
```

```
myapp convert --format json --json-pretty --json-indent 4
myapp convert --format table --table-pretty
myapp convert --format csv
```

Flags of a case that is not selected are ignored. `--format table --json-pretty` binds `Table` with its defaults.

## Argument mode

With `[Argument]` the case is a positional and case flags use bare names. Two cases can share a flag when the property has the same type in both. The selected case decides which property the flag binds.

```csharp
public static void Convert([Argument] Format format) { … }
```

```
myapp convert json --pretty --indent 4
myapp convert table --pretty
```

## Help output

Help lists the cases after the selector, and one row for each case flag:

```
Options:
  --format <case>      [required]
                       One of: <table|json|csv>
  --table-pretty       Applies when --format is table.
  --json-pretty        Applies when --format is json.
  --json-indent <int>  Applies when --format is json. [default: 2]
```

In argument mode a shared flag appears once: `--pretty  Applies when <format> is table or json.`

## Errors

An unknown case name, a missing selector, or a case-flag value that does not parse exits with code `2`:

```
Error: invalid value for --format: 'xml'. Expected one of: table, json, csv.
Error: invalid value for --json-indent: 'abc'.
```

## Supported case property types

Case properties can be `bool`, `string`, `int`, `long`, `float`, `double`, `decimal` or an enum. Enum values bind by their CLI names, including `[EnumValue]` overrides. A `bool` that defaults to `true` gets a `--no-` form, such as `--no-rect-rounded`, because a plain switch can only turn it on.

Any other type, such as `DateTime`, `int?` or an array, is a build error (`AGH0037`). Union selectors and case flags are bound from the command line only, not from environment variables.

## Diagnostics

`AGH0036` (error) reports a case name or case flag that the command claims more than once:

- two cases with the same CLI name, such as `Json` from two namespaces
- a case flag that equals another flag of the command, such as `--json-pretty` next to a `bool jsonPretty` parameter
- in argument mode, one bare flag that two cases declare with different types

`AGH0037` (error) reports a case property whose type case flags cannot bind.
