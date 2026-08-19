<p align="center"><img src="https://cdn.jsdelivr.net/gh/nullean/argh@main/docs/images/png/argh-lockup-348x192.png" alt="--argh_" width="174" height="96"/></p>

# Nullean.Argh.Hosting

[![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Hosting.svg)](https://www.nuget.org/packages/Nullean.Argh.Hosting)

Microsoft.Extensions.Hosting integration for the Nullean.Argh CLI framework.

## Install

```bash
dotnet add package Nullean.Argh.Hosting
```

## Quick Start

```csharp
using Microsoft.Extensions.Hosting;
using Nullean.Argh.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddArgh(args, b =>
{
    b.Map("hello", MyHandlers.SayHello);
});
await builder.Build().RunAsync();
```

## What You Get

- **Dependency injection** — handler parameters resolved from the DI container with proper lifetimes
- **CancellationToken linked to host** — graceful shutdown propagates to your commands automatically
- **Log suppression for intrinsic commands** — `--help`, `--version`, completions, and `__schema` run silently without host startup noise

## Native AOT size

`Microsoft.Extensions.Hosting` (DI, logging, options) adds several MB to a Native AOT binary, independent of Argh — this package is a thin adapter over it. If you don't need DI or hosted-service integration, use `Nullean.Argh` directly and call `app.RunAsync(args)` from `Main` for a much smaller AOT footprint. See [AOT and binary size](https://nullean.github.io/argh/features/aot.html) for measurements.

## Documentation

Full documentation is available at [nullean.github.io/argh](https://nullean.github.io/argh/).

## License

MIT — see [LICENSE](https://github.com/nullean/argh/blob/main/LICENSE) for details.

## Links

- [GitHub](https://github.com/nullean/argh)
- [Documentation](https://nullean.github.io/argh/)
