---
title: Cancellation
---

# CancellationToken

Add `System.Threading.CancellationToken` as a **parameter of the command handler method** (alongside flags and positionals). It is **not** parsed from the command line and does not appear in `--help` — the source generator **injects** the token the runtime uses for cooperative cancellation.

## Handler injection

```csharp
public static async Task<int> Sync(
    string source,
    CancellationToken cancellationToken)
{
    await CopyTreeAsync(source, cancellationToken);
    return 0;
}
// myapp sync --source ./data   (CancellationToken is not a CLI option)
```

## DTO injection

You can also add it on an `[AsParameters]` type as a primary constructor parameter or `init` property (same injection rules):

```csharp
public record RunArgs(string Source, int Port, CancellationToken Ct);

public static async Task<int> Run([AsParameters] RunArgs args)
{
    await Task.Delay(1, args.Ct);
    return 0;
}
```

Keep CLI-bound members first in declaration order: all `[Argument]` positionals must precede flags, and `CancellationToken` must not appear between a flag and a later positional. The usual pattern is to put the token **last** on the DTO.

## Behavior by runtime

### Console app (`ArghApp`)

The token is cancelled when the user presses **Ctrl+C** (and on Windows, the console **break** signal). The process keeps running after cancel unless your handler exits; Argh only forwards cancellation to your code.

### Hosted app (`Nullean.Argh.Hosting`)

The same console token is **linked** with `IHostApplicationLifetime.ApplicationStopping`, so the parameter also cancels when the host is shutting down.

### `TryParseArgh` / generated `TryParseDto_*`

Injected `CancellationToken` members are set to **`default`** — there is no host or console token in that API, so the value is non-cancellable.
