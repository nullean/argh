---
title: Hosted app
---

# Hosted app quick start

Use `Nullean.Argh.Hosting` when the app is already built on `Microsoft.Extensions.Hosting` and you want commands and middleware registered in DI with lifetimes, `CancellationToken` linked to the host, etc.

## Package reference

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Hosting" />
</ItemGroup>
```

## Minimal example

```csharp
using Microsoft.Extensions.Hosting;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArgh(args, b =>
{
    b.Map("hello", MyHandlers.SayHello);
    // b.Map<MyCommandHandlers>(); b.UseGlobalOptions<MyGlobals>(); …
});

await builder.Build().RunAsync();
```

`AddArgh` mirrors the same `Map` / `Map<T>` / `UseGlobalOptions` / `UseNamespaceOptions` / `UseMiddleware` / `MapNamespace` surface as `ArghApp`.

## DI lifetimes

The hosting builder adds APIs for controlling DI lifetimes:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddArgh(args, b =>
{
    b.MapScoped<DeployCommands>();       // resolved per command invocation
    b.UseMiddleware<AuditMiddleware>(ServiceLifetime.Singleton);   // single instance for the process
    b.Map("ping", PingHandlers.Run);    // static method — no DI lifetime needed
    b.UseGlobalOptions<GlobalOptions>();
});
```

| API | Purpose |
|-----|---------|
| `Map<T>()` | Register `T` as transient and add all its public methods as commands. |
| `MapTransient<T>()` / `MapScoped<T>()` / `MapSingleton<T>()` | Same, with an explicit DI lifetime. |
| `UseGlobalOptions<T>()` | Register `T` as the global options type and add it to DI. |
| `UseMiddleware<TMiddleware>()` | Register middleware as transient. |
| `UseMiddleware<TMiddleware>(lifetime)` | Register middleware with an explicit DI lifetime. |

## Exit behavior

`AddArgh` registers a hosted service that runs `ArghRuntime.RunAsync(args)` and then calls `Environment.Exit` with the exit code — the host does not continue after the CLI completes.

Register `AddArgh` before other `IHostedService` registrations if you want the CLI (including `--help`) to run first and exit without starting later background work.

## CancellationToken

With hosting, `CancellationToken` on command handlers is linked to both **Ctrl+C** and **`IHostApplicationLifetime.ApplicationStopping`**, so the parameter also cancels when the host is shutting down.
