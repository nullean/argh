# Nullean.Argh.Interfaces

[![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Interfaces.svg)](https://www.nuget.org/packages/Nullean.Argh.Interfaces)

Zero-dependency contracts and attributes for the Nullean.Argh CLI framework.

## When to Use

Reference this package directly when building **shared libraries** — reusable middleware, custom argument parsers, or shared option types that shouldn't pull in the full runtime.

For applications, use [`Nullean.Argh`](https://www.nuget.org/packages/Nullean.Argh) or [`Nullean.Argh.Hosting`](https://www.nuget.org/packages/Nullean.Argh.Hosting) instead.

## What's Inside

- `IArghBuilder` / `IArghNamespaceBuilder` — builder contracts
- `IArgumentParser<T>` — custom type parser interface
- Middleware interfaces — pipeline extension points
- All attributes — `[Command]`, `[Option]`, `[Positional]`, `[AsParameters]`, and more

## Key Properties

- **Zero external dependencies** — safe to reference from any library
- **netstandard2.0** — compatible with all modern .NET targets

## Documentation

Full documentation is available at [nullean.github.io/argh](https://nullean.github.io/argh/).

## License

MIT — see [LICENSE](https://github.com/nullean/argh/blob/main/LICENSE) for details.

## Links

- [GitHub](https://github.com/nullean/argh)
- [Documentation](https://nullean.github.io/argh/)
