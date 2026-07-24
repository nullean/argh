# Hosted app quick start

Use `Nullean.Argh.Hosting` when your app is built on `Microsoft.Extensions.Hosting` and you want commands and middleware registered in DI with lifetimes and `CancellationToken` linked to the host.

:::::{stepper}

::::{step} Add the package reference

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Hosting" />
</ItemGroup>
```

::::

::::{step} Create a hosted CLI

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

`AddArgh` mirrors the same `Map` / `Map<T>` / `UseGlobalOptions` / `UseNamespaceOptions` / `UseMiddleware` / `MapNamespace` surface as `ArghApp`.

::::

::::{step} Run it

```shell
dotnet run -- hello
```

::::

:::::

## DI lifetimes

The hosting builder adds APIs for controlling DI lifetimes:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddArgh(args, b =>
{
    b.MapScoped<DeployCommands>(); // <1>
    b.UseMiddleware<AuditMiddleware>(ServiceLifetime.Singleton); // <2>
    b.Map("ping", PingHandlers.Run); // <3>
    b.UseGlobalOptions<GlobalOptions>();
});
```

1. Resolved per command invocation.
2. Single instance for the process.
3. Static method - no DI lifetime needed.

:::{dropdown} DI lifetime API reference

| API | Purpose |
|-----|---------|
| `Map<T>()` | Register `T` as transient and add all its public methods as commands. |
| `MapTransient<T>()` / `MapScoped<T>()` / `MapSingleton<T>()` | Same, with an explicit DI lifetime. |
| `UseGlobalOptions<T>()` | Register `T` as the global options type and add it to DI. |
| `UseMiddleware<TMiddleware>()` | Register middleware as transient. |
| `UseMiddleware<TMiddleware>(lifetime)` | Register middleware with an explicit DI lifetime. |

:::

## Exit behavior

`AddArgh` registers a hosted service that runs `ArghRuntime.RunAsync(args)` and then calls `Environment.Exit` with the exit code. The host does not continue after the CLI completes.

:::{tip}
Register `AddArgh` before other `IHostedService` registrations if you want the CLI (including `--help`) to run first and exit without starting later background work.
:::

## CancellationToken

With hosting, `CancellationToken` on command handlers is linked to both **Ctrl+C** and **`IHostApplicationLifetime.ApplicationStopping`**.

This means the token also cancels when the host is shutting down, giving your handlers a unified cancellation signal.
