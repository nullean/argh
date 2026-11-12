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
module internal RunToken =
    /// While targets run side by side, the run's token: a failure elsewhere stops the block, and its commands print
    /// through Console.Out, where each line gets the target's name. Otherwise none, so commands keep the console.
    let current () =
        match Nullean.Make.MakeContext.Current with
        | null -> Threading.CancellationToken.None
        | ctx when isNull ctx.Sink -> Threading.CancellationToken.None
        | ctx -> ctx.CancellationToken

/// <summary>
/// Modifier lines at the top of a <c>target { }</c> block, such as <c>describe "..."</c>, with the rest of the block.
/// </summary>
[<NoComparison; NoEquality>]
type TargetBlock<'TCase, 'T> = internal { Modifiers: (Definition<'TCase> -> Definition<'TCase>) list; Step: ShellStep<'T> option }

type TargetBuilder<'TCase>(dependsOn: 'TCase list, hidden: bool) =
    // Building the block yields a deferred ShellStep; only ShellBuilder.Run executes it, so Run is
    // the one member that differs: it wraps the step in the target's body instead of running it.
    let shell = ShellBuilder(false)
    let definition (step: ShellStep<unit> option) =
        let body = match step with Some step -> (fun _ -> shell.Run(step, RunToken.current ())) | None -> ignore
        FsTarget("", dependsOn, body, hidden)

    /// A modifier on a line of its own, such as <c>describe "Say hello"</c> or <c>needsEnv ["KEY"]</c>.
    member _.Yield(modifier: Definition<'TCase> -> Definition<'TCase>) : TargetBlock<'TCase, unit> =
        { Modifiers = [ modifier ]; Step = None }
    member _.Combine(first: TargetBlock<'TCase, unit>, second: ShellStep<'T>) : TargetBlock<'TCase, 'T> =
        { Modifiers = first.Modifiers; Step = Some second }
    member _.Combine(first: TargetBlock<'TCase, unit>, second: TargetBlock<'TCase, 'T>) : TargetBlock<'TCase, 'T> =
        { Modifiers = first.Modifiers @ second.Modifiers; Step = second.Step }
    // Modifier lines are values, not commands: evaluating them reads the block's metadata and runs nothing. The shell
    // steps after them are still deferred by their own Delay.
    member _.Delay(f: unit -> TargetBlock<'TCase, 'T>) = f ()
    member _.Run(block: TargetBlock<'TCase, unit>) : Definition<'TCase> =
        List.fold (fun d modifier -> modifier d) (definition block.Step) block.Modifiers

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
    member _.Run(step: ShellStep<unit>) : Definition<'TCase> = definition (Some step)

/// <summary>
/// Gives a command a body: a block like <c>target { }</c>'s that runs after every composed target.
/// <code>
/// | Publish -> command {
///                requires [Release]
///                composes [CreateGithubRelease] }
///            |> executes {
///                   let! sha = text $"git rev-parse --short HEAD"
///                   printfn $"published {sha}" }
/// </code>
/// Nothing in the block runs until the command does.
/// </summary>
type ExecutesBuilder() =
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
    member _.Run(step: ShellStep<unit>) : Definition<'TCase> -> Definition<'TCase> =
        let body (_: FsContext) = shell.Run(step, RunToken.current ())
        let rec attach definition =
            match definition with
            | FsCommand(desc, requires, stages, _) -> FsCommand(desc, requires, stages, Some body)
            | FsWith(inner, modifiers) -> FsWith(attach inner, modifiers)
            | _ -> invalidArg "definition" "executes { } gives a command its body; a target has its own block."
        attach

/// What a <c>command { }</c> block collects: its requires and its composed stages in order.
[<NoComparison; NoEquality>]
type CommandSpec<'TCase> = internal { Requires: 'TCase list; Stages: (bool * 'TCase list) list; Description: string option }

/// <summary>
/// Builds a command from <c>requires</c> (gates, skipped under <c>-s</c>), <c>composes</c> (always run) and
/// <c>concurrently</c> stages. Pipe it into <c>executes { }</c> for a body that runs last:
/// <code>
/// | Release -> command {
///       describe "Test, then pack and validate"
///       requires [PristineCheck; Test defaultTest]
///       concurrently [Pkg Generate; Docs Build]
///       composes [Pkg PkgTarget.Validate] }
///   |> executes {
///       let! sha = text $"git rev-parse --short HEAD"
///       printfn $"released {sha}" }
/// </code>
/// </summary>
type CommandBuilder() =
    member _.Yield(()) : CommandSpec<'TCase> = { Requires = []; Stages = []; Description = None }
    /// The description help shows, for a script without an XML documentation file such as an .fsx.
    [<CustomOperation("describe")>]
    member _.Describe(spec: CommandSpec<'TCase>, description: string) = { spec with Description = Some description }
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
    member _.Run(spec: CommandSpec<'TCase>) : Definition<'TCase> =
        let command = FsCommand("", spec.Requires, spec.Stages, None)
        match spec.Description with
        | Some d -> FsWith(command, { Modifiers.empty with Description = Some d })
        | None -> command

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

    /// A command's body, piped in: <c>command { ... } |> executes { sh $"..." }</c>. See ExecutesBuilder.
    let executes = ExecutesBuilder()

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
