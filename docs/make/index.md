# Make

`Nullean.Make` (C#) and `Nullean.Make.Fs` (F#) are typed build target DSLs built on Argh. Targets are union cases
(or DU cases in F#), dependencies run first, and every target gets Argh-style `--help`.

```bash
./build.cs --help
./build.cs test --filter Unit
./build.cs release -s        # skip the gates, run only the composed targets
./build.cs release --graph   # draw the plan and run nothing
```

[**Running targets side by side**](parallel.md) - Parallel stages, `-j`, `--keep-going`, and the output of targets
that run at once.

[**Drawing the plan**](graph.md) - `--graph` draws what a run would do, skipped targets included, and runs nothing.

[**Wrappers**](wrappers.md) - Generate one task per target for [mise](mise.md), [make](makefile.md) and
[just](just.md), and let mise hooks keep them up to date.

The package READMEs cover the target DSLs:

- [`Nullean.Make`](https://github.com/nullean/argh/blob/main/src/Nullean.Make/README.md): C# targets as `union` cases,
  or properties on a `MakeBuild` class.
- [`Nullean.Make.Fs`](https://github.com/nullean/argh/blob/main/src/Nullean.Make.Fs/README.md): F# targets as DU
  cases, with `shell { }` target blocks.
