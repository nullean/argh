# mise

`__wrappers sync` writes `.make/mise-tasks.toml`, a mise task include with one task per target. Pull it in from
`mise.toml`:

```toml
# mise.toml
[task_config]
includes = [".make/mise-tasks.toml"]
```

```bash
mise run test --filter Unit
mise run pkg:generate
mise run test --help        # the script's own help
mise tasks ls
```

To write only this file:

```bash
./build.cs __wrappers sync --only mise
```

## The generated tasks

```toml
["pkg:generate"]
description = "Pack the NuGet packages"
raw_args = true
run = "dotnet run build.cs -- pkg generate"
usage = '''
flag "--configuration <configuration>" help="string (default: Release)"
flag "-s --single-target" help="Skip prerequisite deps; run only the body / Composes"
flag "--graph" help="Draw what would run and run nothing: tree, mermaid or json"
flag "-j --jobs <jobs>" help="How many targets may run at once (default: one per processor)"
flag "-k --keep-going" help="After a failure, finish the targets that do not depend on it"
flag "--output <output>" help="Output of targets running side by side: prefixed or grouped"
flag "--skip <skip>" help="Leave a target out of the plan, with what only it needed"
'''
```

- The `usage` spec lists the target's flags and the global options, so mise completes them.
- `raw_args = true` forwards every argument to the script unchanged, quoting included. Without it, a task that has a
  `usage` spec drops the arguments, and a task without one rejects `--help`.
- A task has no `depends`. The script runs the dependencies.

mise removes the first `--` after the task name, even with `raw_args`. To pass `--` to the script, for a target that
takes the arguments after it, write it twice:

```bash
mise run test -- -- --blame-hang
```

:::{note}
A task defined in `mise.toml` wins over an included task of the same name, and mise prints no warning. When
`mise.toml` already has tasks such as `build` or `test`, pass `--prefix make` to get `make:build` and `make:test`.
:::

## Keeping the files up to date

mise hooks can regenerate the wrapper files, all three of them, so `.make/` can be gitignored. `sync` prints this
snippet when it creates a file:

```toml
# mise.toml
[hooks]
# Runs on every `mise install`, so a fresh clone and CI get the files.
postinstall = "dotnet run build.cs -- __wrappers sync --quiet"

# Runs in a shell with `mise activate`, at the next prompt after build.cs changes.
[[watch_files]]
patterns = ["build.cs"]
run = "dotnet run build.cs -- __wrappers sync --quiet"
```

- `postinstall` runs even when every tool is already installed. It needs no `mise activate`.
- `watch_files` compiles the script, so the first prompt after an edit waits for the compile.
- For a project-based build, list the build project's sources in `patterns`, e.g. `["build/**/*.fs"]`.

See [Wrappers](wrappers.md) for the options and the task names.
