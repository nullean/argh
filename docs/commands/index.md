---
title: Commands
---

# Commands

Commands are the core of any CLI built with Nullean.Argh. The framework supports three registration forms — all fully supported by the source generator.

## Registration forms

```csharp
// 1. Method group — direct typed dispatch.
app.Map("deploy", DeployHandlers.Run);

// 2. Lambda — convenient for simple one-liners.
app.Map("greet", (string name) => Console.WriteLine($"Hello, {name}!"));

// 3. Class — registers every public method on T as a command.
app.Map<StorageHandlers>();
```

With class and method-group registration, XML doc comments on your handler methods flow directly into `--help` output. Lambdas skip that path.

## Registration APIs

| API | Purpose |
|-----|---------|
| `Map(name, handler)` | Bind a command name to a delegate. |
| `Map<T>()` | Register every public method on `T` as a command (typically a static class of handlers). |
| `MapRoot(handler)` | Default handler when no subcommand is given (at app root, or inside a `MapNamespace` callback for that namespace). |

## Routing

Flat apps route `app <command> …`; hierarchical apps route `app <namespace> … <command> …`. The generator emits the switch/dispatch tree accordingly.

For programmatic route inspection, `ArghParser.Route(args)` returns a `RouteMatch` (`CommandPath`, `RemainingArgs`) without invoking handlers — useful for tests and tooling.

## Next steps

- [Registration](registration.md) — detailed registration patterns
- [Namespaces](namespaces.md) — grouping commands under shared paths
