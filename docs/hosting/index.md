---
title: Hosting
---

# Hosting

`Nullean.Argh.Hosting` plugs the same command registration model into `IHost` and `Microsoft.Extensions.DependencyInjection`. No custom bootstrapping or glue code needed.

## Package reference

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Hosting" />
</ItemGroup>
```

## Overview

`services.AddArgh(args, b => { … })` mirrors the same `Map` / `Map<T>` / `UseGlobalOptions` / `UseNamespaceOptions` / `UseMiddleware` / `MapNamespace` surface as `ArghApp`, and additionally lets you control DI lifetimes.

```csharp
using Microsoft.Extensions.Hosting;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArgh(args, b =>
{
    b.Map("hello", MyHandlers.SayHello);
});

await builder.Build().RunAsync();
```

## Key behaviors

:::{important}
`AddArgh` registers a hosted service that runs `ArghRuntime.RunAsync(args)` and then calls `Environment.Exit` with the exit code. The host does not continue after the CLI completes.
:::

- `CancellationToken` on command handlers is linked to both **Ctrl+C** and **`IHostApplicationLifetime.ApplicationStopping`**.
- Register `AddArgh` before other `IHostedService` registrations if you want the CLI to run first and exit without starting later background work.

## Next steps

- [Dependency injection](dependency-injection.md) - DI lifetimes and service resolution
- [Intrinsic commands](intrinsic-commands.md) - log suppression and fast-path for built-in commands
