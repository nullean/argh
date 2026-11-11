# Wrappers

A Make script is its own CLI, but a repository usually has one entry point people already type: `mise run`, `make`
or `just`. `__wrappers sync` writes an include file for each of them from the target graph. You never write a wrapper
task per target, and the include files change when the targets do.

```bash
./build.cs __wrappers sync
```

```
wrote .make/mise-tasks.toml (5 targets)
wrote .make/make.mk (5 targets)
wrote .make/make.just (5 targets)
```

Each page covers how to pull in its file:

- [mise](mise.md): `mise run test --filter Unit`. It has completions, and hooks that regenerate the files.
- [make](makefile.md): `make test ARGS="--filter Unit"`
- [just](just.md): `just test --filter Unit`

The wrappers only forward to the script. The script still resolves and runs the dependencies, so no wrapper declares
any.

## Task names

A target at the root keeps its name. A namespaced target joins its segments with the wrapper's separator:

| Target | mise | make | just |
|---|---|---|---|
| `test` | `test` | `test` | `test` |
| `pkg generate` | `pkg:generate` | `pkg/generate` | `pkg-generate` |
| with `--prefix make` | `make:pkg:generate` | `make/pkg/generate` | `make-pkg-generate` |

`sync` exits `2` when two targets would get the same name in one wrapper, for example `schema-update` and
`schema update` in just. Hidden targets get no task.

## Options

| Option | Purpose |
|---|---|
| `--run <command>` | The command each wrapper runs. A file-based app defaults to `dotnet run build.cs --`. A project-based build passes it, e.g. `--run "dotnet run --project build --"`. |
| `--out-dir <dir>` | Where the files go. Default `.make`. |
| `--only <list>` | Comma-separated subset of `mise`, `make` and `just`. |
| `--prefix <name>` | Groups every task under a name. |
| `--quiet` | Prints nothing unless a file changed. |

Each generated file records its `--run` and `--prefix` in its header. A bare `__wrappers sync` reuses them and
refreshes only the files already in the output directory.

## Keeping the files up to date

[mise hooks](mise.md#keeping-the-files-up-to-date) can regenerate the files on `mise install` and when the script
changes, so `.make/` can be gitignored.

To commit `.make/` instead, check it in CI:

```bash
./build.cs __wrappers check
```

`check` exits `1` and names each file that is missing or out of date.
