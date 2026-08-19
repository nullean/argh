<p align="center"><img src="https://cdn.jsdelivr.net/gh/nullean/argh@main/docs/images/png/argh-lockup-348x192.png" alt="--argh_" width="174" height="96"/></p>

# Nullean.Argh

[![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.svg)](https://www.nuget.org/packages/Nullean.Argh)

A source-generated CLI framework for .NET — no reflection, AOT-safe, with automatic help text from XML docs.

## Install

```bash
dotnet add package Nullean.Argh
```

## Quick Start

```csharp
using Nullean.Argh;

var app = new ArghApp();
app.Map("hello", MyHandlers.SayHello);
return await app.RunAsync(args);
```

## Features

- **XML docs become help text** — document your parameters once, get `--help` for free
- **Source-generated** — no reflection, fully AOT-safe
- **Shell completions** — bash, zsh, and fish out of the box
- **Agent-ready** — JSON schema via `__schema` for LLM/agent integration
- **DataAnnotations validation** — standard .NET validation attributes just work
- **Fuzzy matching** — typo-tolerant command and option matching

## Documentation

Full documentation is available at [nullean.github.io/argh](https://nullean.github.io/argh/).

## License

MIT — see [LICENSE](https://github.com/nullean/argh/blob/main/LICENSE) for details.

## Links

- [GitHub](https://github.com/nullean/argh)
- [Documentation](https://nullean.github.io/argh/)
