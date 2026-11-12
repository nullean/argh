# Conditions, ordering and cleanup

A target can say when its body runs, what it runs after, what cleans up after it, and what it needs before the run
starts. C# adds them to the builder; F# pipes the target through functions.

```csharp
StartDb => app.Target(() => Exec("docker compose up -d db")).Cleanup(new StopDb()),
Test t  => app.Target([new Build(), new StartDb()], () => Exec("dotnet test")).After(new Lint()),
Publish => app.Target(() => Exec("dotnet nuget push build/output/*.nupkg"))
    .OnlyWhen(() => !dryRun.Value)
    .NeedsEnv("NUGET_API_KEY")
    .NeedsTool("docker"),
```

```fsharp
| StartDb -> target [] { sh $"docker compose up -d db" } |> cleanup [StopDb]
| Test o  -> target [Build; StartDb] { sh $"dotnet test" } |> after [Lint]
| Publish -> target [] { sh $"dotnet nuget push build/output/*.nupkg" }
             |> onlyWhen (fun () -> not dryRun.Value)
             |> needsEnv ["NUGET_API_KEY"]
             |> needsTool ["docker"]
             |> describe "Push the packages"
```

In F# the same lines can open a `target { }` block instead of being piped:

```fsharp
| Publish -> target [] {
      describe "Push the packages"
      onlyWhen (fun () -> not dryRun.Value)
      needsEnv ["NUGET_API_KEY"]
      sh $"dotnet nuget push build/output/*.nupkg" }
```

The `MakeBuild` class has the same methods: `.OnlyWhen(...)`, `.After(Lint)`, `.Cleanup(StopDb)`, `.NeedsEnv(...)` and
`.NeedsTool(...)`.

## Conditions

`OnlyWhen` / `onlyWhen` skips the body when the condition returns false. The target's dependencies still run. The log
shows the target as skipped. `--graph` evaluates the condition to show what it skips; a run evaluates it again just
before the target would start.

## Running after a target without depending on it

`After` / `after` orders a target after another one when both are in the run. It does not add the other target to
the run: `package` alone runs only `package`.

It matters where the plan leaves two targets unordered, such as the targets of a parallel stage:

```csharp
Ci => app.Command().Parallel(new Package(), new Lint()),   // package waits for lint
```

A target named with its default value matches the other target with any value; with another value, only that value.
An `After` that contradicts an order the plan already has, such as a composed list that names `package` before
`lint`, is a cycle. The run exits `1` and names the targets involved.

## Cleanup

`Cleanup` / `cleanup` names targets whose bodies run at the end of the run once this target has started, even when
the run fails. Cleanups run newest first, and each runs once however many targets name it. A cleanup runs its body
only, not its dependencies.

```text
Starting  start-db
Starting  integration
[make] Target 'integration' failed: 2 tests failed
Starting  stop-db
Finished  stop-db (1.2s)
```

The run keeps the exit code of the failure. When only a cleanup fails, the run exits with the cleanup's exit code.

## What a target needs

`NeedsEnv` / `needsEnv` and `NeedsTool` / `needsTool` name environment variables that must be set and executables
that must be on the `PATH`. The run checks every target it would run before the first one starts, and lists everything
missing:

```text
[make] The run cannot start. It needs:
  the environment variable NUGET_API_KEY, for publish
  docker on the PATH, for docker image
```

A target whose condition is false needs nothing. `--graph` lists what is missing under `Missing:` and runs nothing.

## Descriptions in F# scripts

A target's description comes from the `///` comment on its case, read from the XML documentation file. An `.fsx`
script has no such file. `describe "..."`, as the first line of a `target { }` or `command { }` block or piped, sets
the description in the script itself.
