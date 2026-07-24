---
title: Shell completions
---

# Shell completions

Tab completion for subcommands, namespaces, and flags is included out of the box. The source generator emits **lookup tables** at compile time (same model as routing and `--help`), and a small `__complete` handler answers the shell with one candidate per line.

:::{note}
`--completions` is not reserved. Use `__completion` / `__complete` only for Argh's integration.
:::

## Commands

| Command | Purpose |
|---------|---------|
| `myapp __completion bash\|zsh\|fish` | Print an install snippet (substitutes your executable name). |
| `myapp __complete <shell> -- <words...>` | Return completion candidates; `words` are argv after the program name. |

## Installation

::::tab-set
:::tab-item Bash
:sync: bash

```bash
eval "$(myapp __completion bash)"
```

Add to `~/.bashrc` to persist.

:::
:::tab-item Zsh
:sync: zsh

```bash
source <(myapp __completion zsh)
```

Add to `~/.zshrc` to persist.

:::
:::tab-item Fish
:sync: fish

Fish 3.4+ required (for `commandline -opc`):

```fish
mkdir -p ~/.config/fish/completions
myapp __completion fish > ~/.config/fish/completions/myapp.fish
```

:::
::::

## How it works

The generator emits completion lookup tables alongside routing and help printers. When the shell invokes `__complete`, the handler walks the command tree using the provided context words and returns matching candidates, one per line.

Completions cover:

- Subcommand and namespace names
- Flag names (including aliases)
- Enum values for typed parameters
