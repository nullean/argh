---
title: Global options
---

# Global options

Share state across commands without repeating parameters on every method signature. Globals are parsed before routing and available to every command.

## Registration

```csharp
public record GlobalOptions(bool Verbose = false);

app.UseGlobalOptions<GlobalOptions>();
app.Map("build", (GlobalOptions g) => { if (g.Verbose) … });
```

Running `myapp build --verbose` applies the global flag.

## How it works

- `UseGlobalOptions<T>()` registers the type as the global options record.
- The generator parses global options **before** routing to a specific command.
- Every command handler can accept the global options type as a parameter.
- Global options appear in the `Global options:` section of `--help` for every command.

## Parsing order

:::{note}
In generated code, parsing follows this order: globals → namespace options along the path → command flags and positionals.
:::

## Combining with `[AsParameters]`

A command can extend global options and annotate it with `[AsParameters]` to inherit those flags alongside its own:

```csharp
public record DeployOptions(string Environment, bool DryRun = false) : GlobalOptions;

app.Map("deploy", ([AsParameters] DeployOptions opts) => { … });
```

Running `myapp deploy --environment staging --dry-run --verbose` passes both the deploy-specific and global flags.
