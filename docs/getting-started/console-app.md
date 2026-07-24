---
title: Console app
---

# Console app quick start

The simplest way to use Nullean.Argh — no dependencies, no host, just a CLI.

## Package reference

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh" />
</ItemGroup>
```

## Minimal example

```csharp
using Nullean.Argh;

var app = new ArghApp();
app.Map("hello", MyHandlers.SayHello);

return await app.RunAsync(args);
```

`RunAsync` dispatches into generated code in your assembly — the source generator emits the parsing, routing, and dispatch logic at build time.

## Registration forms

All three registration forms are supported:

```csharp
using Nullean.Argh;

var app = new ArghApp();

// 1. Method group — direct typed dispatch.
app.Map("deploy", DeployHandlers.Run);

// 2. Lambda — convenient for simple one-liners.
app.Map("greet", (string name) => Console.WriteLine($"Hello, {name}!"));

// 3. Class — registers every public method on T as a command.
app.Map<StorageHandlers>();

return await app.RunAsync(args);
```

## Adding a root command

Use `MapRoot` to define a handler that runs when no subcommand is given:

```csharp
var app = new ArghApp();
app.MapRoot(Handlers.DefaultAction);
app.Map("deploy", Handlers.Deploy);

return await app.RunAsync(args);
```

## App description

For apps without a root command, use `UseCliDescription` to set a one-liner shown beneath the `Usage:` line in `--help`:

```csharp
app.UseCliDescription("Manage and deploy your application's cloud resources.");
```

`UseCliDescription` cannot be combined with `MapRoot` — if you have a root command, put the description in its XML doc `<summary>`.
