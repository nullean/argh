# Choosing what runs

## Several targets

A command line can name several targets. They run in the order they are named, and a dependency they share runs once:

```bash
./build.cs lint test --filter Unit pack
```

Each target takes the arguments up to the next target's name: `test` gets `--filter Unit`. A word stays with the
target before it when it is the value of an option, as in `test --filter pack`, or when that target takes positional
arguments. Everything after `--` stays with the target before it.

The `make` wrapper starts the script once per target: `make test pack` runs it twice.

## Leaving targets out

`-s` / `--single-target` skips the gates of the named target: its dependencies, or a command's requires.

`--skip <target>` leaves one target out of the plan, wherever it appears, with the dependencies that only it needed:

```bash
./build.cs release --skip docs
./build.cs release --skip "pkg validate",docs   # several, separated by commas
./build.cs release --skip pkg/validate           # pkg/validate and pkg:validate work too
```

`--skip` takes a target's name, not a value, so it leaves the target out whatever value it would run with. It exits
`2` for a name that is not a target, with a suggestion, and for a target the command line names itself.

`--graph` shows what both leave out: `skipped: -s` and `skipped: --skip`.
