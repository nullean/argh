# Running targets side by side

A command's composed targets run in order. A parallel stage runs its targets side by side instead: each starts once
the stages before it have finished, and the stages after it wait for all of them.

C#, on a `MakeApp<TUnion>`:

```csharp
Release => app.Command()
    .Requires(new Test())
    .Parallel(new PackNuget(), new DocsBuild(), new DockerImage())
    .Composes(new Publish()),
```

C#, on a `MakeBuild` class:

```csharp
public Command Release => _ => _
    .Requires(Test)
    .Parallel(PackNuget, DocsBuild, DockerImage)
    .Composes(Publish);
```

F#, in a `command { }` block, where the operation is `concurrently` because F# reserves `parallel`:

```fsharp
| Release -> command {
      requires [Test { Filter = None }]
      concurrently [Pkg Generate; Docs Build; Docker Image]
      composes [Publish] }
```

Stages run in the order they are declared. Requires always run before the composed stages. A target two stages share
runs once, the first time it is reached.

`--graph` draws a parallel stage as a `parallel` group. A command's `--help` numbers the targets of one stage the same.

## How many run at once

`-j` / `--jobs` caps how many targets run at once. The default is one per processor. `-j 1` runs a parallel stage one
target at a time, in the order it is declared, which helps when the output of two targets is hard to tell apart.

```bash
./build.cs release -j 4
./build.cs release -j 1
```

## When a target fails

The first failure stops the run. The targets still running are cancelled, and no more start. The run exits with the
failed target's exit code.

`-k` / `--keep-going` lets the targets that do not depend on the failure finish. A target that waits for the failed
one does not start. The run still exits with the first failure's exit code.

A target body stops when it is cancelled only if it watches for it:

- `Shell.Exec` watches for it, and stops the process it started.
- An async body can take the token: `app.Target(async ct => await Task.Delay(1000, ct))`, or read
  `MakeContext.Current.CancellationToken`.

## Output

While targets run side by side, each line of their output carries the target's name:

```text
Starting  pkg generate
Starting  docs build
[pkg generate] Successfully created package 'Example.1.0.0.nupkg'.
[docs build] Build succeeded.
Finished  docs build (4.1s)
Finished  pkg generate (6.3s)

  docs build        4.1s
  pkg generate      6.3s
  Took 6.4s for 10.4s of work
```

`--output grouped` holds a target's lines back and writes them together when it finishes. On GitHub Actions each
target's lines become a collapsible group.

The summary at the end lists each target's time, and the wall-clock time against their sum.

A command run with `Shell.Exec` streams its output through the target's lines. Its output is then a pipe, not a
terminal, so a tool that colours only for a terminal prints without colour. A run with one job, or without a parallel
stage, leaves every command on the console as before.

:::{note}
In F#, `sh` commands inside a `target { }` block run on the console, not through the target's lines, and a failure
elsewhere does not stop them. `printfn` and other `Console` output is prefixed. A parallel F# target finishes the
command it is running when the run stops, and then starts no more.
:::

## Build options with the same name

A global option the build declares itself keeps its name. A build with its own `-k` flag keeps it, and `--keep-going`
is the long form left for the switch.
