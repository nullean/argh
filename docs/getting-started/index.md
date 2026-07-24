---
title: Getting started
---

# Getting started

Nullean.Argh provides two entry points depending on your application model:

- **[Console app](console-app.md)** — use `Nullean.Argh` directly with `ArghApp` for lightweight CLIs with no external dependencies.
- **[Hosted app](hosted-app.md)** — use `Nullean.Argh.Hosting` when your app is built on `Microsoft.Extensions.Hosting` and you want DI, lifetimes, and host integration.

Both share the same registration surface (`Map`, `Map<T>`, `MapRoot`, `MapNamespace`, etc.) and the same source generator. The difference is how the runtime is bootstrapped and whether DI is available.

## Which package do I need?

| Scenario | Package |
|----------|---------|
| Simple CLI tool, no DI needed | `Nullean.Argh` |
| App uses `IHost`, needs DI, logging, config | `Nullean.Argh.Hosting` |
| Building a shared middleware/parser library | `Nullean.Argh.Interfaces` |
