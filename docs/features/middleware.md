---
title: Middleware
---

# Middleware

Cross-cutting logic — auth checks, logging, timing — lives in middleware and stays out of handler methods.

## Implementing middleware

Implement `ICommandMiddleware`:

```csharp
public class TimingMiddleware : ICommandMiddleware
{
    public async Task InvokeAsync(CommandContext ctx, Func<Task> next)
    {
        var sw = Stopwatch.StartNew();
        await next();
        Console.Error.WriteLine($"{ctx.CommandPath}: {sw.ElapsedMilliseconds}ms");
    }
}
```

## Global middleware

Runs for every command:

```csharp
app.UseMiddleware<TimingMiddleware>();
```

## Per-handler middleware

Apply via attribute on the method:

```csharp
[MiddlewareAttribute<TimingMiddleware>]
public static Task<int> Deploy(string environment) { … }
```

## CommandContext

`ICommandMiddleware` receives `CommandContext` with:

- `CommandPath` — the matched command path
- `Args` — the raw arguments
- `ExitCode` — settable exit code
- `CancellationToken` — the cancellation token for the invocation

## Pipeline behavior

- Middleware does **not** run for `--help`, `--version`, `__completion`, `__complete`, or `__schema`
- The pipeline is wired in generated code — not a runtime delegate chain
- Each middleware call is emitted as a direct invocation in the generated dispatch method; there is no runtime list to build or iterate

## DI integration

When using `Nullean.Argh.Hosting`, middleware types are resolved from the DI container. Control lifetimes with the overload:

```csharp
builder.Services.AddArgh(args, b =>
{
    b.UseMiddleware<AuditMiddleware>(ServiceLifetime.Singleton);
});
```

Without a host, middleware falls back to `new T()` when no `IServiceProvider` is available.
