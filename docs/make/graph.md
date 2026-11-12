# Drawing the plan

`--graph` draws what a run would do, and runs nothing. It plans the run exactly as the target would: it binds the
target's arguments, applies `-s`, and evaluates each `OnlyWhen` condition. A target the run would skip shows as skipped,
with the reason.

```bash
./build.cs release --graph
```

```text
release  Test, then pack
├─ requires
│  ├─ pristine-check
│  └─ test
│     └─ build
│        └─ clean
└─ composes
   ├─ pkg generate
   └─ pkg validate

Runs: pristine-check → clean → build → test → pkg generate → pkg validate
```

`Runs:` lists the targets whose bodies run, in order. A target that two others depend on runs once; the tree draws it
the first time and marks later references `(above)`.

## Skipped targets

With `-s`, the named target's gates show as skipped and their own dependencies drop out of the plan:

```bash
./build.cs release -s --graph
```

```text
release  Test, then pack
├─ requires
│  ├─ pristine-check  skipped: -s
│  └─ test  skipped: -s
└─ composes
   ├─ pkg generate
   └─ pkg validate

Runs: pkg generate → pkg validate
```

A target whose condition is false shows `skipped: its condition is false`. Its dependencies still run, as they do in a
real run. `--graph` evaluates the condition when it draws the plan; a real run evaluates it again just before the
target would start.

## Dependencies with values

A dependency that names a value runs with that value, and the plan shows it after the route:

```text
ci
└─ composes
   ├─ test (filter: Unit)
   └─ test (filter: Integration)
```

Two references with equal values are one target in the plan, and run once. Two references with different values run
once each. Only the target named on the command line binds the command line's arguments.

## Formats

| Command | Prints |
|---|---|
| `--graph` or `--graph tree` | The tree and the `Runs:` line |
| `--graph mermaid` | A Mermaid flowchart, with one edge per ordering and skipped targets dashed |
| `--graph json` | The plan as JSON: `roots`, then `targets` in run order with their stages and what each waits for |

`--graph=mermaid` works too. `--graph` without a target draws every target that nothing else depends on.

A target's `--help` lists the same order under `Runs:` when the target has dependencies.
