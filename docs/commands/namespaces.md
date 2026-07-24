# Namespaces

Group related commands under a shared path, scoped options, and their own help page. This follows the same mental model as ASP.NET's `MapGroup`.

## Basic usage

The idiomatic pattern is to put commands as methods on a class, then register the class with `AddNamespace<T>`:

```csharp
/// <summary>Commands under <c>storage</c>.</summary>
internal sealed class StorageCommands
{
    /// <summary>List objects in the bucket.</summary>
    public void List() => Console.WriteLine("storage:list");
}

app.AddNamespace<StorageCommands>("storage");
```

This creates the path: `storage list`.

## Nested namespaces

Nested sub-groups are registered explicitly inside the namespace callback. There is no auto-discovery of nested types.

```csharp
/// <summary>Commands under <c>storage</c>.</summary>
internal sealed class StorageCommands
{
    /// <summary>List objects in the bucket.</summary>
    public void List() => Console.WriteLine("storage:list");
}

/// <summary>Commands under <c>storage blob</c>.</summary>
internal sealed class BlobCommands
{
    /// <summary>Upload a file.</summary>
    /// <param name="path">-p,--path, Local file path.</param>
    public void Upload(string path) => Console.WriteLine($"storage:blob:upload:{path}");

    /// <summary>Download a file.</summary>
    /// <param name="key">-k,--key, Object key.</param>
    public void Download(string key) => Console.WriteLine($"storage:blob:download:{key}");
}

app.AddNamespace<StorageCommands>("storage", ns =>
{
    ns.AddNamespace<BlobCommands>("blob"); // <1>
});
```

1. Register a nested namespace inside the parent callback.

This creates the following paths:

- `storage list`
- `storage blob upload --path ./file.txt`
- `storage blob download --key backups/db.sql`

## Namespace options

Add `CommandNamespaceOptions<T>()` inside the callback to attach scoped options to a namespace and its children:

```csharp
app.AddNamespace<StorageCommands>("storage", ns =>
{
    ns.CommandNamespaceOptions<StorageOptions>(); // <1>
    ns.AddNamespace<BlobCommands>("blob");
});
```

1. Scoped options are inherited by all commands within the namespace, including nested sub-namespaces.

See [Namespace options](../advanced/namespace-options.md) for details on the inheritance chain.

## XML docs on namespace classes

Put the `<summary>` (and optionally `<remarks>`) on the class `T` passed to `AddNamespace<T>`. The generator uses it as the namespace description in `myapp storage --help` and in the root command listing.

```csharp
/// <summary>Manage blob and file storage resources.</summary>
/// <remarks>
/// Requires a storage connection string via <c>--connection-string</c>
/// or the <c>STORAGE_CONN</c> environment variable.
/// </remarks>
internal sealed class StorageCommands { … }

app.AddNamespace<StorageCommands>("storage", ns => { … });
```

:::{tip}
The generator produces separate help printers for the namespace overview and each leaf command. This means `<summary>` and `<remarks>` on the namespace class only appear in the namespace-level help, not in individual command help.
:::
