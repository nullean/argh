# Nullean.Argh.Interfaces

![Nullean.Argh](https://raw.githubusercontent.com/nullean/argh/main/docs/images/png/argh-lockup-348x192.png)

The contracts and attributes of Nullean.Argh, for libraries that share middleware, argument parsers or option types
without pulling in the runtime.

[![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Interfaces?style=flat-square)](https://www.nuget.org/packages/Nullean.Argh.Interfaces)

## Install

```sh
dotnet add package Nullean.Argh.Interfaces
```

Reference it directly when building **shared libraries**: reusable middleware, custom argument parsers, or shared
option types. For applications, use [Nullean.Argh](https://www.nuget.org/packages/Nullean.Argh) or
[Nullean.Argh.Hosting](https://www.nuget.org/packages/Nullean.Argh.Hosting) instead.

## Quick start

A custom argument parser, which any Argh app can use for its parameters:

```csharp
using Nullean.Argh.Parsing;

public readonly record struct Port(int Value);

public sealed class PortParser : IArgumentParser<Port>
{
	public bool TryParse(string raw, out Port value)
	{
		var ok = int.TryParse(raw, out var n) && n is > 0 and < 65536;
		value = new Port(ok ? n : 0);
		return ok;
	}
}
```

## What's inside

- `IArghBuilder` and `IArghNamespaceBuilder`: the builder contracts.
- `IArgumentParser<T>`: the custom type parser interface.
- `ICommandMiddleware` and the middleware delegates: pipeline extension points.
- All attributes: `[Argument]`, `[AsParameters]`, `[ArgumentParser]`, `[CommandName]`, `[DefaultCommand]`,
  `[Hidden]`, `[Existing]`, `[Env]` and more.

It targets `netstandard2.0`, `net8.0` and `net11.0`, and depends on nothing beyond the BCL
(`System.Threading.Tasks.Extensions` on `netstandard2.0`), so it is safe to reference from any library.

## Learn more

- Documentation: https://argh.nullean.net
- Source and issues: https://github.com/nullean/argh
