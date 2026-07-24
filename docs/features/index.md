---
title: Features
---

# Features

Nullean.Argh includes several built-in features that require no extra configuration:

- **[Help](help.md)** — XML doc comments become rich `--help` output automatically
- **[Completions](completions.md)** — tab completion for bash, zsh, and fish out of the box
- **[Schema](schema.md)** — machine-readable JSON description of your entire CLI
- **[Middleware](middleware.md)** — cross-cutting logic (auth, timing, logging) without polluting handlers
- **[Fuzzy matching](fuzzy-matching.md)** — typos produce actionable errors with suggestions

All features are generated at build time — no runtime reflection, no dynamic dispatch, no external dependencies.
