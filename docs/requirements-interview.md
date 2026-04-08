# Nullean.Argh — requirements interview

This document captures **open product questions** for `Nullean.Argh` (zero-dependency core) and a **Hosting**-integrated package. It is informed by [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) as a reference point, including gaps discussed in Cysharp issues/PRs and your direction on exclusions (e.g. no automatic JSON parsing).

**How to use:** Answer inline under each question, or reply in chat/issue with section + question numbers (e.g. `3.4`, `4.2`). Partial answers are fine; mark *TBD* where undecided.

---

## 1. Packages and dependencies

1.1. Confirm the split: **`Nullean.Argh`** = BCL-only (no `Microsoft.Extensions.*`), **`Nullean.Argh.Hosting`** = deep integration with Hosting / DI / Options / Logging. Any third package (e.g. analyzers-only, or samples)?

1.2. Should **Hosting** take a **hard dependency** on a specific `Microsoft.Extensions.*` major line (align with LTS TFMs), or multi-target?

1.3. Is the core generator allowed to emit code that **references** Hosting types only when a compile-time switch or reference to `Nullean.Argh.Hosting` exists (conditional generation), or are the two generators completely separate?

1.4. For consumers who only reference the core package: do you guarantee **no transitive** `Microsoft.Extensions.*` packages?

---

## 2. Command model and declaration style

2.1. Primary authoring model: **static methods**, **instance methods on registered types**, **minimal API–style lambdas**, **records/classes as handlers**, or multiple supported with one “blessed” style?

2.2. **Groups / subcommands:** Define the exact grammar you want (e.g. `app module`, `app module sub`, `app module-sub` flat vs nested). How do groups relate to **types** (one class per group) vs **methods**?

2.3. Nesting depth: max levels, and should **middleware/filters** apply per group, per leaf command, or both?

2.4. **`[AsParameters]` / aggregate binding** (as in [CAF PR #237](https://github.com/Cysharp/ConsoleAppFramework/pull/237)): adopt under the same or a different attribute name? Support **prefix** for multiple aggregates on one handler?

2.5. **Typed global options** + **inheritance** into command parameter types: required for v1 or later?

2.6. **Explicit non-goals vs CAF:** Which CAF features do you refuse to carry (e.g. certain APIs, JSON, specific attributes)? List them.

---

## 3. Parsing, types, and validation

3.1. **Positional arguments** vs **options**: rules for order, required positionals, optional trailing args, `params`-like remainder (`--` separator)?

3.2. **Flags:** Short form (`-v`), long form (`--verbose`), combined short flags (`-abc`), equals vs space (`--out=file` vs `--out file`)?

3.3. **Collections:** `IEnumerable`, `List<T>`, `T[]` — comma-separated, **repeated flags** ([CAF PR #239](https://github.com/Cysharp/ConsoleAppFramework/pull/239)), or both? Per-option policy?

3.4. **Enums:** string parsing only, case sensitivity, and **help text** for allowed values ([CAF #234](https://github.com/Cysharp/ConsoleAppFramework/issues/234)): always, never, or opt-in per enum / attribute?

3.5. **No automatic JSON:** Confirm scope: e.g. never bind `JsonDocument` / deserialize objects from a single string argument unless an explicit opt-in attribute? Are **file paths** to JSON still “just strings” with no framework parsing?

3.6. **Built-in conversions:** `int`, `bool`, `DateTime`, `TimeSpan`, `Guid`, `Uri` — which are in v1?

3.7. **Validation:** source-generated guards only, hook for `IValidatableObject`, integration with **Options** validation in Hosting package?

3.8. **Culture:** invariant-only parsing vs current culture for dates/numbers?

---

## 4. Help, documentation, and UX

4.1. **Hierarchical help** ([CAF #242](https://github.com/Cysharp/ConsoleAppFramework/issues/242)): At root, list **only** top-level groups/commands (summary). For `group --help`, list **only** that group’s children. Confirm this is the baseline; any escape hatch to dump “flat” all commands?

4.2. **Summary vs long description** ([CAF #230](https://github.com/Cysharp/ConsoleAppFramework/issues/230)): e.g. first line of `<summary>` in parent lists; full text + `<remarks>` only on leaf `--help`? Alternative: attributes instead of XML docs?

4.3. **XML doc rendering** ([CAF #231](https://github.com/Cysharp/ConsoleAppFramework/issues/231)): Should `<para>`, `<code>`, `<see cref>` be rendered for terminal help, stripped to plain text, or ignored with a separate “docs” channel?

4.4. **Line wrapping and width:** respect `Console.WindowWidth`, fixed width, or configurable?

4.5. **Usage line:** always `appname command [options]` with explicit subcommand path? Placeholder for executable name from `Assembly` / host?

4.6. **Version:** `--version` semantics and assembly informational version (MinVer etc.) — core vs Hosting?

4.7. **Discoverability for LLMs** (#242 context): any dedicated `app docs` / JSON schema export for commands, or out of scope?

---

## 5. Execution, lifecycle, and platform

5.1. **Sync vs async** handlers: `Task`, `ValueTask`, `async void` forbidden?

5.2. **Cancellation:** `CancellationToken` from host vs `Console.CancelKeyPress` only in core?

5.3. **Exit codes:** convention (0 success, 1 usage, 2 validation?), mapper from exception types, user-returned `int`?

5.4. **Exceptions:** print message only, stack trace behind `--verbose`, never dump stack?

5.5. **AOT / trimming:** same bar as CAF (generated code only, no reflection in hot path) or documented subset?

5.6. **Target frameworks** for core (e.g. `netstandard2.0` for Roslyn) vs app TFMs you care about (`net8`, `net9`, `net10`)?

---

## 6. Hosting integration (`Nullean.Argh.Hosting`)

6.1. Entry API: `Host.CreateApplicationBuilder`, `HostApplicationBuilder` extensions, or both? Generic host vs minimal hosting?

6.2. **DI:** resolve handlers as transient/scoped/singleton? Constructor injection on handler types?

6.3. **`IOptions<T>`:** bind global options and/or command models from configuration section names — conventions?

6.4. **`ILogger` / `ILogger<T>`:** inject by default into handlers? Log level switches wired to options?

6.5. **`IConfiguration`:** explicit binding attributes vs convention-based?

6.6. **Console lifetime** (`AddConsoleLifetime` etc.): integrate shutdown with running command?

6.7. **Background services:** can a CLI command start long-running hosted services, or CLI is always foreground-only in v1?

---

## 7. Extensibility, testing, and tooling

7.1. **Middleware / filters** (before parse, after parse, on error): required in v1 or post-v1?

7.2. **Replacing** pieces: pluggable tokenizer/parser vs fixed generated parser?

7.3. **Testing:** public API to invoke parser with `string[]` / memory console; snapshot tests for help output?

7.4. **Analyzers:** package name for Roslyn diagnostics (duplicate commands, invalid binding)? Same repo as generators?

7.5. **Source of truth** for command tree: single generator pass over assembly, or multi-assembly scanning?

---

## 8. Explicit exclusions and principles

8.1. **No automatic JSON** (and similar): list other formats or conveniences you refuse (YAML, env-var auto-prefixing, interactive prompts, shell completion generation).

8.2. **Performance vs simplicity:** any non-negotiables (zero allocation in parse path, UTF-8 span-based, etc.)?

8.3. **License and governance:** MIT like the rest of Nullean; contribution / API review expectations?

---

## 9. Milestones (for later roadmap)

9.1. What is **MVP** (smallest shippable): core only, one sample app, Hosting alpha?

9.2. What can wait until **v0.2 / v1**?

---

When answers are filled in, this file (or a copy) can be promoted to `docs/product-spec.md` and traced to issues.
