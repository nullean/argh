---
title: Intrinsic commands
---

# Intrinsic commands and log suppression

When the host starts up, configuration providers, logging infrastructure, and other services initialize before the CLI runs. This means commands like `--help` or `--version` can be preceded by startup noise in the output.

`AddArgh` addresses this automatically. If the invocation is an **intrinsic command** - a built-in (`--help`, `-h`, `--version`, `__schema`, `__completion`, `__complete`) or a user-defined method marked `[CommandIntrinsic]` - it configures logging to suppress entries below `Warning` before the host builds.

## User-defined intrinsic commands

Mark any handler method `[CommandIntrinsic]` to opt it into the same log suppression. These commands still run through the full host and DI because they may need services:

```csharp
public class InfoCommands
{
    private readonly IVersionService _version;
    public InfoCommands(IVersionService version) => _version = version;

    /// <summary>Print runtime version and environment info.</summary>
    [CommandIntrinsic]
    [CommandName("info")]
    public void Info() => Console.WriteLine(_version.Current);
}

builder.Services.AddArgh(args, b => b.Map<InfoCommands>());
```

Running `dotnet run -- info` produces output with no startup log noise.

## Override the suppression threshold

The default minimum level is `Warning` (suppresses `Information` and below). Override it on the hosting builder:

```csharp
builder.Services.AddArgh(args, b =>
{
    b.IntrinsicLogLevelMinimum(LogLevel.Trace); // <1>
    b.Map<InfoCommands>();
});
```
1. Re-enables all log levels for intrinsic commands

## Pre-host fast path

If host startup is expensive and you want zero overhead for the built-in intrinsic commands, call `ArghApp.TryArghIntrinsicCommand(args)` *before* `Host.CreateApplicationBuilder`. If the invocation is a built-in, the command runs and the process exits immediately. The host is never constructed.

:::{tip}
For user-defined `[CommandIntrinsic]` commands (which need DI), log suppression is the right tool instead of the pre-host fast path.
:::

```csharp
await ArghApp.TryArghIntrinsicCommand(args); // <1>

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddArgh(args, b => { b.Map<InfoCommands>(); });
await builder.Build().RunAsync();
```
1. Built-ins exit here with no host overhead: `--help`, `--version`, `__schema`, etc.

:::{note}
If `TryArghIntrinsicCommand` is omitted, there is no breakage. The built-ins still work correctly; they are just handled inside the hosted service with log suppression active.
:::
