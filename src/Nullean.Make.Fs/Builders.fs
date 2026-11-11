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
type TargetBuilder<'TCase>(dependsOn: 'TCase list) =
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
    member _.Run(step: ShellStep<unit>) : Definition<'TCase> = FsTarget("", dependsOn, fun _ -> shell.Run step)

/// What a <c>command { }</c> block collects: its requires, its composes and an optional body.
[<NoComparison; NoEquality>]
type CommandSpec<'TCase> = internal { Requires: 'TCase list; Composes: 'TCase list; Body: (unit -> unit) option }

/// <summary>
/// Builds a command from <c>requires</c> (gates, skipped under <c>-s</c>), <c>composes</c> (always run) and an
/// optional <c>executes</c> body that runs last:
/// <code>
/// | Release -> command {
///       requires [PristineCheck; Test defaultTest]
///       composes [Pkg Generate; Pkg PkgTarget.Validate]
///       executes (
///           shell {
///               let! sha = text $"git rev-parse --short HEAD"
///               printfn $"released {sha}" }) }
/// </code>
/// </summary>
type CommandBuilder() =
    member _.Yield(()) : CommandSpec<'TCase> = { Requires = []; Composes = []; Body = None }
    /// Targets that must succeed first. Skipped under -s.
    [<CustomOperation("requires")>]
    member _.Requires(spec: CommandSpec<'TCase>, targets: 'TCase list) = { spec with Requires = spec.Requires @ targets }
    /// Targets the command always runs, in order.
    [<CustomOperation("composes")>]
    member _.Composes(spec: CommandSpec<'TCase>, targets: 'TCase list) = { spec with Composes = spec.Composes @ targets }
    /// A body that runs after every composed target. Deferred: nothing in it runs until the command does.
    [<CustomOperation("executes")>]
    member _.Executes(spec: CommandSpec<'TCase>, [<ProjectionParameter>] body: unit -> unit) = { spec with Body = Some body }
    member _.Run(spec: CommandSpec<'TCase>) : Definition<'TCase> =
        FsCommand("", spec.Requires, spec.Composes, spec.Body |> Option.map (fun body -> fun _ -> body ()))

/// <summary>The <c>target</c> and <c>command</c> builders. Descriptions come from the DU case's <c>///</c> doc comment.</summary>
[<AutoOpen>]
module MakeBuilders =
    /// <summary>
    /// A target that runs its block after <paramref name="dependsOn"/> (skipped under <c>-s</c>):
    /// <code>
    /// | Build -> target [Clean] { exec { run "dotnet" ["build"; "-c"; "Release"] } }
    /// </code>
    /// </summary>
    let target (dependsOn: 'TCase list) = TargetBuilder<'TCase>(dependsOn)

    /// A command that sequences other targets. See CommandBuilder.
    let command = CommandBuilder()
