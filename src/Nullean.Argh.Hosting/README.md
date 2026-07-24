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

## Documentation

Full documentation is available at [nullean.github.io/argh](https://nullean.github.io/argh/).

## License

MIT — see [LICENSE](https://github.com/nullean/argh/blob/main/LICENSE) for details.

## Links

- [GitHub](https://github.com/nullean/argh)
- [Documentation](https://nullean.github.io/argh/)
