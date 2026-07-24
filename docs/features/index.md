# Features

Every feature listed here is implemented at compile time by the Roslyn source generator. No reflection runs at runtime. No separate tooling step is required.

## Code generation

[**Source-generated**](source-generated.md) - The generator emits typed dispatch, parsers, help printers, and completion tables directly into your assembly. You can read, debug, and step through the generated code.

[**AOT and trimming safe**](aot.md) - Zero reflection means your CLI publishes cleanly with Native AOT or the IL trimmer. No special configuration needed.

## Help and discovery

[**XML docs are your help text**](help.md) - Write `<summary>`, `<param>`, `<remarks>`, and `<example>` once on your handler methods. They appear in `--help` automatically.

[**Shell completions**](completions.md) - Tab completion for commands, namespaces, and flags. One install command for bash, zsh, or fish.

[**Fuzzy matching**](fuzzy-matching.md) - Typos produce actionable error messages with the correct qualified command path and a `--help` suggestion.

## Data and validation

[**DataAnnotations validation**](validation.md) - Standard .NET validation attributes (`[Range]`, `[StringLength]`, `[AllowedValues]`, `[Existing]`) generate inline checks. Constraints appear in help output.

[**DTO binding**](dto-binding.md) - Annotate a record or class with `[AsParameters]` to expand its members into individual CLI flags. Supports prefixes and inheritance.

## Schema and tooling

[**Agent-ready JSON schema**](schema.md) - `myapp __schema` emits a full JSON description of your CLI conforming to [cli-schema v1](https://github.com/cli-schema/cli-schema). Feed it to LLMs, generate docs, or diff in CI.

## Runtime behavior

[**Middleware**](middleware.md) - Cross-cutting concerns (timing, auth, logging) live in `ICommandMiddleware` implementations. Wire them globally or per-command with `[MiddlewareAttribute<T>]`.

[**Cancellation**](cancellation.md) - Add `CancellationToken` to any handler signature. It tracks Ctrl+C in console apps, or links to host shutdown with `Nullean.Argh.Hosting`.
