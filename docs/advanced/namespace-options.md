---
title: Namespace options
---

# Namespace options

Scoped to a namespace and its children. Namespace options extend the shared state model beyond global options for hierarchical CLIs.

## Registration

```csharp
public record StorageOptions(string ConnectionString = "") : GlobalOptions;

app.MapNamespace<StorageHandlers>("storage", ns =>
{
    ns.UseNamespaceOptions<StorageOptions>();
    ns.Map("list", (StorageOptions o) => { … });
});
// myapp storage list --connection-string "…" --verbose
```

## Inheritance chain

The options type must inherit the parent's options type — `GlobalOptions` at the root, or the enclosing namespace's options further down. The generator reports an error (**AGH0004**) if the chain is broken.

```csharp
public record GlobalOptions(bool Verbose = false);
public record StorageOptions(string ConnectionString = "") : GlobalOptions;
public record BlobOptions(string Container = "") : StorageOptions;

app.UseGlobalOptions<GlobalOptions>();
app.MapNamespace<StorageHandlers>("storage", ns =>
{
    ns.UseNamespaceOptions<StorageOptions>();
    ns.MapNamespace<BlobHandlers>("blob", blob =>
    {
        blob.UseNamespaceOptions<BlobOptions>();
    });
});
```

## Parsing order

In generated code: globals → namespace options along the path → command flags and positionals.

## Required parameter injection

Commands under a namespace are required to declare the namespace options type as a parameter (enforced by analyzer **AGH0021**). Annotate the method with `[NoOptionsInjection]` to opt out.

## Combining with `[AsParameters]`

```csharp
public record DeployOptions(string Environment, bool DryRun = false) : StorageOptions;

ns.Map("deploy", ([AsParameters] DeployOptions opts) => { … });
// myapp storage deploy --connection-string "…" --environment staging --dry-run
```
