# CancellationToken

Add `System.Threading.CancellationToken` as a parameter of your command handler method. It is not parsed from the command line and does not appear in `--help`. The source generator injects the token the runtime uses for cooperative cancellation.

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

You can also add it on an `[AsParameters]` type as a primary constructor parameter or `init` property:

```csharp
public record RunArgs(string Source, int Port, CancellationToken Ct);

public static async Task<int> Run([AsParameters] RunArgs args)
{
    await Task.Delay(1, args.Ct);
    return 0;
}
```

:::{tip}
Keep CLI-bound members first in declaration order. All `[Argument]` positionals must precede flags, and `CancellationToken` must not appear between a flag and a later positional. Put the token **last** on the DTO.
:::

## Behavior by runtime

::::{tab-set}

:::{tab-item} Console app (ArghApp)
The token is cancelled when the user presses **Ctrl+C** (and on Windows, the console break signal). The process keeps running after cancel unless your handler exits. Argh only forwards cancellation to your code.
:::

:::{tab-item} Hosted app (Nullean.Argh.Hosting)
The same console token is linked with `IHostApplicationLifetime.ApplicationStopping`, so the parameter also cancels when the host is shutting down.
:::

:::{tab-item} TryParseArgh / TryParseDto
Injected `CancellationToken` members are set to `default`. There is no host or console token in that API, so the value is non-cancellable.
:::

::::
