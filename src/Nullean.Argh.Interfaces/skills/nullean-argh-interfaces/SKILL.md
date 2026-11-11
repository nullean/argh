---
name: nullean-argh-interfaces
description: >-
  Use when writing a reusable library for Nullean.Argh CLIs: a custom argument parser (IArgumentParser<T>), command
  middleware (ICommandMiddleware) or shared attributes, without depending on the runtime.
---

# Nullean.Argh.Interfaces

The contracts and attributes of Nullean.Argh, with no dependencies, on netstandard2.0. Reference it from a shared
library; an app references `Nullean.Argh` or `Nullean.Argh.Hosting`, which bring it in.

## The calls you need

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Nullean.Argh.Middleware;
using Nullean.Argh.Parsing;

public readonly struct Port
{
	public Port(int value) => Value = value;
	public int Value { get; }
}

// A parser for a type Argh does not bind itself. Return false to report an invalid value.
public sealed class PortParser : IArgumentParser<Port>
{
	public bool TryParse(string raw, out Port value)
	{
		value = default;
		if (!int.TryParse(raw, out var n) || n is < 1 or > 65535) return false;
		value = new Port(n);
		return true;
	}
}

// Middleware wraps every command it is registered for; call next to run the command.
public sealed class TimingMiddleware : ICommandMiddleware
{
	public async ValueTask InvokeAsync(CommandContext context, CommandMiddlewareDelegate next)
	{
		var sw = Stopwatch.StartNew();
		await next(context);
		Console.Error.WriteLine($"{context.CommandName} took {sw.ElapsedMilliseconds} ms (exit {context.ExitCode})");
	}
}
```

In the app:

```csharp
app.UseMiddleware<TimingMiddleware>();   // every command; [Middleware<TimingMiddleware>] on a method for one
app.Map("serve", Handlers.Serve);

public static class Handlers
{
	/// <summary>Serves on a port.</summary>
	/// <param name="port">-p, --port, Port to listen on.</param>
	public static void Serve([ArgumentParser(typeof(PortParser))] Port port) => Console.WriteLine(port.Value);
}
```

## Gotchas

- `IArgumentParser<T>.TryParse` is an instance method. The parser needs a public parameterless constructor.
- A `false` from the parser exits `2` with `invalid value for --port`. Middleware does not run when parsing fails.
- `CommandContext.ExitCode` is the command's exit code after `next` returns; set it, or call `ReportError`, to fail
  from middleware without calling `next`.
- netstandard2.0 has no `record` or `init` without a polyfill (`IsExternalInit`); use plain types in a shared
  library, as above.

Docs: https://argh.nullean.net
