namespace Nullean.Make.Fs

open System
open Proc.Fs

/// <summary>
/// Builds a target from a Proc.Fs <c>shell { }</c> block that runs only when the target runs:
/// <code>
/// | Build -> target [Clean] {
///       sh $"dotnet build -c {config.Value}"
///       let! sha = text $"git rev-parse HEAD"
///       use! _ = inDir "src"
///       sh $"dotnet pack" }
/// </code>
/// Everything <c>shell { }</c> accepts works: <c>sh $"…"</c> commands, <c>let!</c> captures (<c>text</c>, <c>lines</c>,
/// <c>exitCode</c>, <c>succeeds</c>), steps such as <c>cd</c>, <c>pushd</c>, <c>env</c> and <c>inDir</c>, and plain F#
/// including <c>exec { }</c>, <c>if</c>, <c>match</c> and loops. The first failing command fails the target.
/// Created by <see cref="M:Nullean.Make.Fs.MakeBuilders.target"/>.
/// </summary>
type TargetBuilder<'TCase>(dependsOn: 'TCase list, hidden: bool) =
    // Building the block yields a deferred ShellStep; only ShellBuilder.Run executes it, so Run is
    // the one member that differs: it wraps the step in the target's body instead of running it.
    let shell = ShellBuilder(false)
    member _.Zero() = shell.Zero()
    member _.Yield(command: Cmd) = shell.Yield command
    member _.Yield(step: ShellStep<unit>) = shell.Yield step
    member _.Bind(step: ShellStep<'T>, f: 'T -> ShellStep<'U>) = shell.Bind(step, f)
    member _.Bind(command: Cmd, f: unit -> ShellStep<'U>) = shell.Bind(command, f)
    member _.Bind(capture: Capture<'T>, f: 'T -> ShellStep<'U>) = shell.Bind(capture, f)
    member _.Delay(f: unit -> ShellStep<'T>) = shell.Delay f
    member _.Combine(first: ShellStep<unit>, second: ShellStep<'T>) = shell.Combine(first, second)
    member _.For(items: seq<'T>, body: 'T -> ShellStep<unit>) = shell.For(items, body)
    member _.For(capture: Capture<'T list>, body: 'T -> ShellStep<unit>) = shell.For(capture, body)
    member _.While(guard: unit -> bool, body: ShellStep<unit>) = shell.While(guard, body)
    member _.TryWith(body: ShellStep<'T>, handler: exn -> ShellStep<'T>) = shell.TryWith(body, handler)
    member _.TryFinally(body: ShellStep<'T>, compensation: unit -> unit) = shell.TryFinally(body, compensation)
    member _.Using(resource: 'R :> IDisposable, body: 'R -> ShellStep<'T>) = shell.Using(resource, body)
    member _.Run(step: ShellStep<unit>) : Definition<'TCase> =
        FsTarget("", dependsOn, (fun _ -> shell.Run(step, TargetBuilder<'TCase>.Token())), hidden)

    /// While targets run side by side, the run's token: a failure elsewhere stops the block, and its commands print
    /// through Console.Out, where each line gets the target's name. Otherwise none, so commands keep the console.
    static member internal Token() =
        match Nullean.Make.MakeContext.Current with
        | null -> Threading.CancellationToken.None
        | ctx when isNull ctx.Sink -> Threading.CancellationToken.None
        | ctx -> ctx.CancellationToken

/// What a <c>command { }</c> block collects: its requires, its composed stages in order and an optional body.
[<NoComparison; NoEquality>]
type CommandSpec<'TCase> = internal { Requires: 'TCase list; Stages: (bool * 'TCase list) list; Body: (unit -> unit) option }

/// <summary>
/// Builds a command from <c>requires</c> (gates, skipped under <c>-s</c>), <c>composes</c> (always run) and an
/// optional <c>executes</c> body that runs last:
/// <code>
/// | Release -> command {
///       requires [PristineCheck; Test defaultTest]
///       concurrently [Pkg Generate; Docs Build]
///       composes [Pkg PkgTarget.Validate]
///       executes (
///           shell {
///               let! sha = text $"git rev-parse --short HEAD"
///               printfn $"released {sha}" }) }
/// </code>
/// </summary>
type CommandBuilder() =
    member _.Yield(()) : CommandSpec<'TCase> = { Requires = []; Stages = []; Body = None }
    /// Targets that must succeed first. Skipped under -s.
    [<CustomOperation("requires")>]
    member _.Requires(spec: CommandSpec<'TCase>, targets: 'TCase list) = { spec with Requires = spec.Requires @ targets }
    /// Targets the command always runs, in order.
    [<CustomOperation("composes")>]
    member _.Composes(spec: CommandSpec<'TCase>, targets: 'TCase list) =
        match List.rev spec.Stages with
        | (false, last) :: earlier -> { spec with Stages = List.rev ((false, last @ targets) :: earlier) }
        | _ -> { spec with Stages = spec.Stages @ [false, targets] }
    /// <summary>
    /// Targets that run side by side, as one stage: each starts once the stages before it have finished, and the
    /// stages after it wait for all of them. Stages run in the order they are written. <c>-j</c> caps how many run at once.
    /// The name is <c>concurrently</c> because F# reserves <c>parallel</c>.
    /// </summary>
    [<CustomOperation("concurrently")>]
    member _.Concurrently(spec: CommandSpec<'TCase>, targets: 'TCase list) = { spec with Stages = spec.Stages @ [true, targets] }
    /// A body that runs after every composed target. Deferred: nothing in it runs until the command does.
    [<CustomOperation("executes")>]
    member _.Executes(spec: CommandSpec<'TCase>, [<ProjectionParameter>] body: unit -> unit) = { spec with Body = Some body }
    member _.Run(spec: CommandSpec<'TCase>) : Definition<'TCase> =
        FsCommand("", spec.Requires, spec.Stages, spec.Body |> Option.map (fun body -> fun _ -> body ()))

/// <summary>The <c>target</c> and <c>command</c> builders. Descriptions come from the DU case's <c>///</c> doc comment.</summary>
[<AutoOpen>]
module MakeBuilders =
    /// <summary>
    /// A target that runs its block after <paramref name="dependsOn"/> (skipped under <c>-s</c>):
    /// <code>
    /// | Build -> target [Clean] { exec { run "dotnet" ["build"; "-c"; "Release"] } }
    /// </code>
    /// </summary>
    let target (dependsOn: 'TCase list) = TargetBuilder<'TCase>(dependsOn, false)

    /// <summary>
    /// An internal step: a <c>target</c> that help leaves out. It still runs when named and when another target
    /// depends on it, so a compile step shared by several targets need not be listed:
    /// <code>
    /// | Compile -> step [] { sh $"dotnet build" }
    /// </code>
    /// </summary>
    let step (dependsOn: 'TCase list) = TargetBuilder<'TCase>(dependsOn, true)

    /// A command that sequences other targets. See CommandBuilder.
    let command = CommandBuilder()

/// <summary>
/// Pipe a <c>target</c> or <c>command</c> through these to add to it:
/// <code>
/// | Publish -> target [] { sh $"dotnet nuget push build/output/*.nupkg" }
///              |> onlyWhen (fun () -> not dryRun.Value)
///              |> needsEnv ["NUGET_API_KEY"]
///              |> describe "Push the packages"
/// </code>
/// </summary>
[<AutoOpen>]
module MakeModifiers =
    /// <summary>
    /// Skips the body when <paramref name="condition"/> returns false. The dependencies still run. <c>--graph</c>
    /// evaluates it to show what it skips; a run evaluates it again just before the target would start.
    /// </summary>
    let onlyWhen (condition: unit -> bool) (definition: Definition<'TCase>) =
        definition |> Modifiers.add (fun m -> { m with OnlyWhen = Some condition })

    /// <summary>
    /// Runs after <paramref name="targets"/> when they are in the same run, without adding them to it. A case with its
    /// default payload matches the target with any payload; with another payload, only that payload.
    /// </summary>
    let after (targets: 'TCase list) (definition: Definition<'TCase>) =
        definition |> Modifiers.add (fun m -> { m with After = m.After @ targets })

    /// <summary>
    /// Targets whose bodies run at the end of the run, newest first, once this target has started, even when the run
    /// fails. A cleanup runs its body only, and once however many targets name it.
    /// </summary>
    let cleanup (targets: 'TCase list) (definition: Definition<'TCase>) =
        definition |> Modifiers.add (fun m -> { m with Cleanup = m.Cleanup @ targets })

    /// Environment variables that must be set. The run checks them before anything starts, and lists every one missing.
    let needsEnv (variables: string list) (definition: Definition<'TCase>) =
        definition |> Modifiers.add (fun m -> { m with NeedsEnv = m.NeedsEnv @ variables })

    /// Executables that must be on the PATH. The run checks them before anything starts, and lists every one missing.
    let needsTool (tools: string list) (definition: Definition<'TCase>) =
        definition |> Modifiers.add (fun m -> { m with NeedsTool = m.NeedsTool @ tools })

    /// The description help shows, for a script without an XML documentation file such as an .fsx.
    let describe (description: string) (definition: Definition<'TCase>) =
        definition |> Modifiers.add (fun m -> { m with Description = Some description })
