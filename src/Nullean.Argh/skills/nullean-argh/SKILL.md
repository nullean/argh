---
name: nullean-argh
description: >-
  Use when building or changing a .NET command-line app with Nullean.Argh. Methods become commands, their XML doc
  comments become --help, and a source generator emits all parsing and dispatch, so the app is AOT-safe.
---

# Nullean.Argh

A source-generated CLI framework: register handler methods on an `ArghApp`, and the generator turns their parameters
into flags and positionals at build time, with help text from `///` comments. For DI and the generic host, use
`Nullean.Argh.Hosting` instead (skill `nullean-argh-hosting`).

## The calls you need

```csharp
using Nullean.Argh;

var app = new ArghApp();
app.UseGlobalOptions<GlobalOptions>();
app.Map("greet", Handlers.Greet);            // a method group: its XML docs become --help
app.Map("copy", Handlers.Copy);
app.MapNamespace<StorageCommands>("storage"); // `storage list`: every public method is a command
return await app.RunAsync(args);

/// <summary>Options every command accepts.</summary>
public sealed class GlobalOptions
{
	/// <summary>-v, --verbose, Print more output.</summary>
	public bool Verbose { get; set; }
}

public static class Handlers
{
	/// <summary>Greets someone by name.</summary>
	/// <param name="name">-n, --name, Who to greet.</param>
	/// <param name="times">How many times.</param>
	public static int Greet(GlobalOptions g, string name, int times = 1)
	{
		for (var i = 0; i < times; i++) Console.WriteLine($"hello {name}");
		return 0;
	}

	/// <summary>Copies files.</summary>
	/// <param name="dest">Destination directory.</param>
	/// <param name="files">Files to copy.</param>
	public static async Task<int> Copy(GlobalOptions g, [Argument] string dest, [Argument] string[] files, CancellationToken ct)
	{
		await Task.Delay(1, ct);
		return files.Length == 0 ? 1 : 0;
	}
}

/// <summary>Blob storage.</summary>
public sealed class StorageCommands(GlobalOptions g)
{
	/// <summary>Lists objects.</summary>
	/// <param name="prefix">Only objects under this prefix.</param>
	public void List(string? prefix = null) => Console.WriteLine($"list {prefix}");
}
```

- `myapp greet -n Ann --times 2 -v`, `myapp copy out a.txt b.txt`, `myapp storage list --prefix logs/`.
- A parameter is a `--kebab-case` flag. One without a default is `[required]`, and `[Argument]` makes it positional.
  A trailing `[Argument] T[]` collects the rest.
- A handler returns `void`, `int`, `Task` or `Task<int>`; the `int` is the exit code. A `CancellationToken`
  parameter is cancelled on Ctrl+C.
- `<param>` text starting with `-x, --long,` sets the short name and replaces the derived long name. On an options
  class, the same goes into the property's `<summary>`.
- `[AsParameters]` binds a record's constructor parameters as flags (`[AsParameters("app")]` prefixes them `--app-`).

## Gotchas

- Once `UseGlobalOptions<T>()` is registered, every method-group command must take `T` as a parameter, or as a
  constructor parameter of its class. Otherwise the build fails with `AGH0021`.
- A lambda (`app.Map("ping", (string host) => ...)`) works, but it has no XML docs, so it gets no description and no
  flag descriptions in `--help`. Use a method for anything users read help for.
- Global options bind from the `<summary>` of settable properties on a class. A positional record's `<param>` docs
  give no short names or descriptions there.
- `UseMiddleware` takes a type (`UseMiddleware<T>()`); an inline delegate is not emitted (`AGH0006`).
- Exit code `2` means a parse error (unknown command, missing or invalid value). Argh prints the error, the flag's
  help line and a "did you mean" suggestion itself, so do not catch it.
- `myapp __schema` prints a JSON description of the whole CLI; `myapp __completion bash|zsh|fish` prints a completion
  script.

Docs: https://argh.nullean.net
