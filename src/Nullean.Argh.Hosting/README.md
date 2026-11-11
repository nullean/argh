# Nullean.Argh.Hosting

![Nullean.Argh](https://raw.githubusercontent.com/nullean/argh/main/docs/images/png/argh-lockup-348x192.png)

Microsoft.Extensions.Hosting integration for Nullean.Argh: CLI commands resolved from DI, with Ctrl+C linked to host
shutdown.

[![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Hosting?style=flat-square)](https://www.nuget.org/packages/Nullean.Argh.Hosting)

## Install

```sh
dotnet add package Nullean.Argh.Hosting
dotnet add package Microsoft.Extensions.Hosting
```

## Quick start

```csharp
using Microsoft.Extensions.Hosting;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddArgh(args, b =>
{
	b.Map("hello", Handlers.Hello);
});
await builder.Build().RunAsync();

static class Handlers
{
	/// <summary>Greets someone by name.</summary>
	/// <param name="name">-n,--name, Name to greet.</param>
	public static void Hello(string name) => Console.WriteLine($"Hello, {name}!");
}
```

`AddArgh` mirrors the `Map`, `Map<T>`, `UseGlobalOptions`, `UseNamespaceOptions`, `UseMiddleware` and `MapNamespace`
surface of `ArghApp`.

## What you get

- **Dependency injection**: handler parameters resolved from the DI container with proper lifetimes.
- **CancellationToken linked to the host**: graceful shutdown propagates to your commands automatically.
- **Log suppression for intrinsic commands**: `--help`, `--version`, completions and `__schema` run silently without
  host startup noise.

## Native AOT size

`Microsoft.Extensions.Hosting` (DI, logging, options) adds several MB to a Native AOT binary, independent of Argh; this
package is a thin adapter over it. If you don't need DI or hosted-service integration, use
[Nullean.Argh](https://www.nuget.org/packages/Nullean.Argh) directly and call `app.RunAsync(args)` from `Main` for a
much smaller AOT footprint. See [AOT and binary size](https://argh.nullean.net/features/aot) for measurements.

## Learn more

- Documentation: https://argh.nullean.net
- Source and issues: https://github.com/nullean/argh
