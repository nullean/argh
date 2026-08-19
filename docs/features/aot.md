# Native AOT and trimming

Nullean.Argh is AOT-safe and trimming-safe by default. The source generator emits all parsing, routing, dispatch, and help code directly into your assembly at build time. No reflection, no dynamic dispatch.

## Guarantees

- **No reflection** - all type binding, parameter parsing, and dispatch is generated as static C# code
- **No dynamic dispatch** - the generated switch tree routes directly to handler methods
- **Trimming-safe** - no hidden dependencies that the trimmer might remove
- **AOT-compatible** - works with `PublishAot=true` without any special configuration

## DI and AOT

When using `Nullean.Argh.Hosting` with native AOT, register handler and middleware types explicitly in DI so required constructors are preserved:

```csharp
builder.Services.AddScoped<DeployCommands>();
builder.Services.AddSingleton<AuditMiddleware>();
builder.Services.AddArgh(args, b =>
{
    b.MapScoped<DeployCommands>();
    b.UseMiddleware<AuditMiddleware>(ServiceLifetime.Singleton);
});
```

## Lambda handlers

:::{warning}
Lambda handlers (`UseMiddleware` inline delegates) are the one exception to the zero-reflection rule and emit warning **AGH0006**. Prefer method groups or class registration for AOT-published apps.
:::

## Binary size

`Nullean.Argh.Core` has no dependency on `System.Text.Json` or any other reflection-based serializer. The `__schema` command (and `ArghRuntime.FormatCliSchemaJson()`) is served by a small hand-rolled, write-only JSON emitter, since the schema document is the only JSON ever produced by Argh and it is never deserialized at runtime.

Measured on a minimal `app.Map()` + one no-op handler, `PublishAot=true`, macOS arm64:

| Binary | Size | Delta vs. blank `PublishAot` exe (1.10 MB) |
|---|---|---|
| Blank `PublishAot` console app | 1.10 MB | - |
| + `Nullean.Argh` (no `Hosting`) | 1.48 MB | +0.37 MB |
| + `Nullean.Argh.Hosting` (`AddArgh`) | 8.77 MB | +7.67 MB |

An [ILC size report](https://github.com/kant2002/MstatAnalyser) of the `Nullean.Argh`-only build shows Argh's own code (`Nullean.Argh`, `Nullean.Argh.Schema`, `Nullean.Argh.Help`, `Nullean.Argh.Matching`, `Nullean.Argh.Runtime` namespaces combined) accounts for well under 50 KB of that 0.37 MB. The rest is baseline .NET runtime machinery (`System.Threading.Tasks` for the async entry point, the reflection/type-loader stack the runtime keeps available for stack traces and `GetType()`, `System.Globalization`) that any Native AOT console app using `Task`-returning `Main` pays.

**`Nullean.Argh.Hosting` is the largest lever if you need a small binary.** It exists purely to integrate with the .NET Generic Host (DI, `IHostedService`, logging). That integration is a thin adapter (~700 lines) — the +7.67 MB comes from `Microsoft.Extensions.Hosting`/`DependencyInjection`/`Logging`/`Options` themselves, which is a well-known Native AOT size cost independent of Argh. If you don't need DI or hosted-service integration, skip `Nullean.Argh.Hosting` and call `app.RunAsync(args)` directly from `Main` — you keep the ~0.4 MB profile above.

If you still need Hosting (or want to shave more off a bare `Nullean.Argh` app) and can accept the trade-offs, these standard Native AOT feature switches compound with the above (measured on the bare `Nullean.Argh` app):

```xml
<PropertyGroup>
  <!-- Argh's generated parsing/formatting code already uses CultureInfo.InvariantCulture
       throughout, so this is behavior-neutral for Argh-based CLIs. -167 KB. -->
  <InvariantGlobalization>true</InvariantGlobalization>
  <!-- Drops the method-name table used to symbolicate stack traces on unhandled
       exceptions. Only set this if you don't rely on readable stack traces in
       production crash logs. -166 KB. -->
  <StackTraceSupport>false</StackTraceSupport>
</PropertyGroup>
```

Together these cut roughly another 280 KB (~17%) off the `Nullean.Argh`-only binary. `IlcOptimizationPreference=Size`, `UseSystemResourceKeys=true`, `EventSourceSupport=false`, and `HttpActivityPropagationSupport=false` were also measured and made no measurable difference for a typical Argh CLI (no networking, no `EventSource` usage), so they aren't listed above.

## CI validation

The project includes an `aot-validate` CI job that publishes with Native AOT on Linux, macOS, and Windows and invokes `__schema` on the native binary to verify correct operation.

## Generated code target

Generated code targets `netstandard2.0` (no `System.Linq`, no pattern features beyond what that TFM allows). The static extension methods in `ArghTypeBindingExtensions.g.cs` require C# 14 preview (`static extension` members).
