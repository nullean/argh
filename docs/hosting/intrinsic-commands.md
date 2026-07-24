---
title: Intrinsic commands
---

# Intrinsic commands and log suppression

When the host starts up, configuration providers, logging infrastructure, and other services initialize before the CLI runs. This means commands like `--help` or `--version` can be preceded by startup noise in the output.

`AddArgh` addresses this automatically: if the invocation is an **intrinsic command** — a built-in (`--help`, `-h`, `--version`, `__schema`, `__completion`, `__complete`) or a user-defined method marked `[CommandIntrinsic]` — it configures logging to suppress entries below `Warning` before the host builds.

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
// dotnet run -- info  →  no startup log noise
```

## Override the suppression threshold

The default minimum level is `Warning` (suppresses `Information` and below). Override it on the hosting builder:

```csharp
builder.Services.AddArgh(args, b =>
{
    b.IntrinsicLogLevelMinimum(LogLevel.Trace);   // re-enable all logs for intrinsic commands
    b.Map<InfoCommands>();
});
```

## Pre-host fast path

If host startup is expensive and you want zero overhead for the built-in intrinsic commands, call `ArghApp.TryArghIntrinsicCommand(args)` *before* `Host.CreateApplicationBuilder`. If the invocation is a built-in, the command runs and the process exits immediately — the host is never constructed.

For user-defined `[CommandIntrinsic]` commands (which need DI), log suppression is the right tool instead.

```csharp
// Built-ins exit here with no host overhead: --help, --version, __schema, etc.
await ArghApp.TryArghIntrinsicCommand(args);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddArgh(args, b => { b.Map<InfoCommands>(); });
await builder.Build().RunAsync();
```

If `TryArghIntrinsicCommand` is omitted, there is no breakage — the built-ins still work correctly; they are just handled inside the hosted service with log suppression active.
