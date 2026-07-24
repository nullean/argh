---
title: AOT
---

# Native AOT and trimming

Nullean.Argh is AOT-safe and trimming-safe by default. The source generator emits all parsing, routing, dispatch, and help code directly into your assembly at build time — no reflection, no dynamic dispatch.

## Guarantees

- **No reflection** — all type binding, parameter parsing, and dispatch is generated as static C# code
- **No dynamic dispatch** — the generated switch tree routes directly to handler methods
- **Trimming-safe** — no hidden dependencies that the trimmer might remove
- **AOT-compatible** — works with `PublishAot=true` without any special configuration

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

Lambda handlers (`UseMiddleware` inline delegates) are the one exception to the zero-reflection rule and emit a warning (**AGH0006**). Prefer method groups or class registration for AOT-published apps.

## CI validation

The project includes an `aot-validate` CI job that publishes with Native AOT on Linux, macOS, and Windows and invokes `__schema` on the native binary to verify correct operation.

## Generated code target

Generated code targets `netstandard2.0` (no `System.Linq`, no pattern features beyond what that TFM allows), except the static extension methods in `ArghTypeBindingExtensions.g.cs` which require C# 14 preview (`static extension` members).
