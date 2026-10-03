# Middleware

Cross-cutting logic - auth checks, logging, timing - lives in middleware and stays out of handler methods.

## Implementing middleware

Implement `ICommandMiddleware`:

```csharp
public class TimingMiddleware : ICommandMiddleware
{
    public async ValueTask InvokeAsync(CommandContext ctx, CommandMiddlewareDelegate next)
    {
        var sw = Stopwatch.StartNew();
        await next(ctx);
        Console.Error.WriteLine($"{ctx.CommandName}: {sw.ElapsedMilliseconds}ms");
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

- `CommandPath` - the matched command path
- `Args` - the raw arguments
- `ExitCode` - settable exit code
- `CancellationToken` - the cancellation token for the invocation
- `Arguments` - the bound option objects the handler receives (`[AsParameters]` parameters, global and namespace options), each a `CommandArgument` with its `Value` and a `MemberFlags` map from member name to flag. Lets middleware validate what was parsed instead of re-parsing `Args`
- `ReportError(message, flag)` - writes `Error: --flag: message` to stderr and sets `ExitCode` to 2, like the generated parser. Do not call `next` afterwards

## Pipeline behavior

:::{note}
Middleware does **not** run for `--help`, `--version`, `__completion`, `__complete`, or `__schema`.
:::

The pipeline is wired in generated code, not a runtime delegate chain. Each middleware call is emitted as a direct invocation in the generated dispatch method. There is no runtime list to build or iterate.

## DI integration

When using `Nullean.Argh.Hosting`, middleware types are resolved from the DI container. Control lifetimes with the overload:

```csharp
builder.Services.AddArgh(args, b =>
{
    b.UseMiddleware<AuditMiddleware>(ServiceLifetime.Singleton);
});
```

:::{tip}
Without a host, middleware falls back to `new T()` when no `IServiceProvider` is available.
:::
