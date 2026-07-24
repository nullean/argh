---
title: Dependency injection
---

# Dependency injection

When using `Nullean.Argh.Hosting`, DI integration is fully transparent. Register your handler and middleware types in the service collection and the generated code resolves them automatically. No manual `ServiceProvider` wiring needed.

## Handler with injected service

```csharp
public class DeployCommands(IDeployService deployer)
{
    public async Task<int> Run(string environment)
    {
        await deployer.DeployAsync(environment);
        return 0;
    }
}
```

```csharp
builder.Services.AddScoped<IDeployService, DeployService>(); // <1>
builder.Services.AddArgh(args, b => b.Map<DeployCommands>()); // <2>
```
1. The service must be registered in the DI container
2. Handler type is registered and its public methods become commands

## DI lifetime APIs

| API | Purpose |
|-----|---------|
| `Map<T>()` | Register `T` as transient and add all its public methods as commands. |
| `MapTransient<T>()` / `MapScoped<T>()` / `MapSingleton<T>()` | Same, with an explicit DI lifetime. |
| `UseGlobalOptions<T>()` | Register `T` as the global options type and add it to DI. |
| `UseMiddleware<TMiddleware>()` | Register middleware as transient. |
| `UseMiddleware<TMiddleware>(lifetime)` | Register middleware with an explicit DI lifetime. |

## Example with lifetimes

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddArgh(args, b =>
{
    b.MapScoped<DeployCommands>();       // <1>
    b.UseMiddleware<AuditMiddleware>(ServiceLifetime.Singleton);   // <2>
    b.Map("ping", PingHandlers.Run);    // <3>
    b.UseGlobalOptions<GlobalOptions>();
});
```
1. Resolved per command invocation
2. Single instance for the process
3. Static method - no DI lifetime needed

## Without hosting

:::{dropdown} Advanced: using DI without Nullean.Argh.Hosting

For advanced use or when not using `Nullean.Argh.Hosting`: `ArghServices.ServiceProvider` is typed as `System.IServiceProvider` and set when running under a host.

For `Map<T>()` instance methods and `UseMiddleware<T>()` / `[MiddlewareAttribute<T>]`, generated code resolves via `GetService(typeof(T))` when a provider is present. Otherwise it falls back to `new T()`.
:::

## AOT considerations

:::{warning}
For native AOT / trimming, register handler and middleware types explicitly in DI so required constructors are preserved.
:::
