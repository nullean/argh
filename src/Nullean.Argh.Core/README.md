# Nullean.Argh.Core

![Nullean.Argh](https://raw.githubusercontent.com/nullean/argh/main/docs/images/png/argh-lockup-348x192.png)

The shared runtime and embedded source generator of Nullean.Argh, the .NET CLI framework where methods become commands
and XML docs become help text.

[![NuGet](https://img.shields.io/nuget/v/Nullean.Argh.Core?style=flat-square)](https://www.nuget.org/packages/Nullean.Argh.Core)

## Install

You typically **don't reference this package directly**. It comes in transitively with
[Nullean.Argh](https://www.nuget.org/packages/Nullean.Argh) (console apps) or
[Nullean.Argh.Hosting](https://www.nuget.org/packages/Nullean.Argh.Hosting) (hosted apps):

```sh
dotnet add package Nullean.Argh
```

## Quick start

`ArghApp`, the entry point of every Argh CLI, lives in this package:

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

## What's inside

- **ArghApp**: the CLI application entry point.
- **ArghRuntime**: command dispatch and execution engine.
- **ArghParser**: argument tokenization and binding.
- **Help rendering**: automatic `--help` output from XML docs and attributes.
- **Schema generation**: a JSON schema for agent and LLM integration.
- **Roslyn source generator**: runs at build time and emits all parsing, dispatch and help code, with zero runtime
  reflection.

## Learn more

- Documentation: https://argh.nullean.net
- Source and issues: https://github.com/nullean/argh
