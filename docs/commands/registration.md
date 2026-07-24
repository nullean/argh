---
title: Registration
---

# Command registration

## Map(name, handler)

Bind a command name to a delegate — method group or lambda:

```csharp
app.Map("deploy", DeployHandlers.Run);
app.Map("greet", (string name) => Console.WriteLine($"Hello, {name}!"));
```

The command name determines the CLI invocation path: `myapp deploy …`, `myapp greet …`.

## Map&lt;T&gt;()

Register every public method on `T` as a command. Method names are converted to kebab-case:

```csharp
app.Map<StorageHandlers>();
```

If `StorageHandlers` has methods `List()` and `Upload(string path)`, this creates commands `myapp list` and `myapp upload --path …`.

## MapRoot(handler)

Define a default handler when no subcommand is given:

```csharp
app.MapRoot(Handlers.DefaultAction);
```

At the app level this handles bare `myapp` invocations. Inside a `MapNamespace` callback, it handles `myapp <namespace>` without a subcommand.

When using `MapRoot`, the XML doc on the handler method becomes the app-level overview in `--help`:

```csharp
/// <summary>Manage and deploy your application's cloud resources.</summary>
/// <remarks>
/// Run <c>myapp &lt;command&gt; --help</c> for details on any command.
/// </remarks>
public static Task<int> Root() { … }

app.MapRoot(Root);
```

## UseCliDescription

For apps with no default root command, use `UseCliDescription` to set a plain one-liner shown beneath the `Usage:` line:

```csharp
app.UseCliDescription("Manage and deploy your application's cloud resources.");
```

`UseCliDescription` is on `IArghRootBuilder` (not `IArghBuilder`), so it is unavailable inside `MapNamespace` configure callbacks. It cannot be combined with `MapRoot`; the generator reports `AGH0023` if both are present.
