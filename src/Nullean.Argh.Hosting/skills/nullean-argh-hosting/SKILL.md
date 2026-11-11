---
name: nullean-argh-hosting
description: >-
  Use when a Nullean.Argh CLI needs dependency injection, logging or configuration from the .NET generic host.
  AddArgh runs the CLI as a hosted service, resolves command classes from DI and links CancellationToken to shutdown.
---

# Nullean.Argh.Hosting

Runs a Nullean.Argh CLI inside `Microsoft.Extensions.Hosting`: command classes get their constructor dependencies
from the service collection. If you need no DI, use `Nullean.Argh` alone: the host adds several MB to a native AOT
binary.

## The calls you need

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddArgh(args, b =>
{
	b.Map<DeployCommands>(ServiceLifetime.Scoped); // public methods become commands; the class comes from DI
	b.Map("ping", PingHandlers.Ping);              // static method groups work as in Nullean.Argh
});
await builder.Build().RunAsync();

/// <summary>Deployment commands.</summary>
public sealed class DeployCommands(IClock clock)
{
	/// <summary>Deploys to an environment.</summary>
	/// <param name="environment">-e, --environment, Where to deploy.</param>
	/// <param name="ct">Cancelled on Ctrl+C or host shutdown.</param>
	public async Task<int> Deploy(string environment, CancellationToken ct)
	{
		await Task.Delay(10, ct);
		Console.WriteLine($"deployed to {environment} at {clock.Now:u}");
		return 0;
	}
}
```

- `b.Map<T>()` registers `T` as transient; `b.Map<T>(ServiceLifetime.X)` picks the lifetime.
- `b.UseGlobalOptions<T>()` and `b.UseMiddleware<T>()` also register the type in DI. Both have an overload that takes
  a `ServiceLifetime`.
- `--help`, `--version`, completions and `__schema` run with host logging turned down
  (`b.IntrinsicLogLevelMinimum(...)` changes the level).

## Gotchas

- Use `b.Map<T>(ServiceLifetime.Scoped)`, not `MapScoped<T>()`. `MapScoped`, `MapTransient` and `MapSingleton`
  compile, but the source generator only reads `Map<T>`, so their commands never exist (`unknown command`).
- The process exits through `Environment.Exit` with the command's exit code once the command finishes. Code after
  `RunAsync()` does not run, and `StopAsync` is not called on hosted services.
- Call `AddArgh` before other `AddHostedService` registrations. Hosted services registered earlier start on every
  invocation, `--help` included; those registered after it never start.
- The `configure` callback must list the same commands on every run. The generator reads it at build time; it is not a
  place for runtime conditions.

Docs: https://argh.nullean.net
