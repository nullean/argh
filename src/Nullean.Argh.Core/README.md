# Nullean.Argh.Core

[![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Core.svg)](https://www.nuget.org/packages/Nullean.Argh.Core)

Shared runtime and embedded source generator for the Nullean.Argh CLI framework.

## When to Use

You typically **don't reference this package directly**. It's pulled in transitively by:

- [`Nullean.Argh`](https://www.nuget.org/packages/Nullean.Argh) (console apps)
- [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) (hosted apps)

## What's Inside

- **ArghApp** — the CLI application entry point
- **ArghRuntime** — command dispatch and execution engine
- **ArghParser** — argument tokenization and binding
- **Help rendering** — automatic `--help` output from XML docs and attributes
- **Schema generation** — JSON schema for agent/LLM integration
- **Roslyn source generator** — runs at build time, emits all parsing, dispatch, and help code with zero runtime reflection

## Documentation

Full documentation is available at [nullean.github.io/argh](https://nullean.github.io/argh/).

## License

MIT — see [LICENSE](https://github.com/nullean/argh/blob/main/LICENSE) for details.

## Links

- [GitHub](https://github.com/nullean/argh)
- [Documentation](https://nullean.github.io/argh/)
