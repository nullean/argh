# Nullean.Argh

![Nullean.Argh](https://raw.githubusercontent.com/nullean/argh/main/docs/images/png/argh-lockup-348x192.png)

Build full-featured .NET CLIs without writing a parser: methods become commands, XML docs become help text.
Source-generated, no reflection, AOT-safe.

[![NuGet](https://img.shields.io/nuget/v/Nullean.Argh?style=flat-square)](https://www.nuget.org/packages/Nullean.Argh)

![Sample CLI help output (XmlDocShowcase)](https://raw.githubusercontent.com/nullean/argh/main/docs/assets/xml-doc-showcase-help.gif)

## Install

```sh
dotnet add package Nullean.Argh
```

## Quick start

```csharp
using Nullean.Argh;

var app = new ArghApp();
app.Map("hello", Handlers.Hello);

return await app.RunAsync(args);

static class Handlers
{
	/// <summary>Greets someone by name.</summary>
	/// <param name="name">-n,--name, Name to greet.</param>
	public static void Hello(string name) => Console.WriteLine($"Hello, {name}!");
}
```

`dotnet run -- hello --name Argh` prints `Hello, Argh!`, and `dotnet run -- hello --help` prints the XML docs as help.

## Features

- **XML docs become help text**: document your parameters once, get `--help` for free.
- **Source-generated**: no reflection, fully AOT-safe.
- **Shell completions**: bash, zsh and fish out of the box.
- **Agent-ready**: a JSON schema via `__schema` for LLM and agent integration.
- **DataAnnotations validation**: standard .NET validation attributes just work.
- **Fuzzy matching**: typo-tolerant command and option matching.

This package is a metapackage: it brings in [Nullean.Argh.Core](https://www.nuget.org/packages/Nullean.Argh.Core) and
[Nullean.Argh.Interfaces](https://www.nuget.org/packages/Nullean.Argh.Interfaces), and no `Microsoft.Extensions.*`
dependency. For `Microsoft.Extensions.Hosting` and DI, use [Nullean.Argh.Hosting](https://www.nuget.org/packages/Nullean.Argh.Hosting).

## Learn more

- Documentation: https://argh.nullean.net
- Source and issues: https://github.com/nullean/argh
