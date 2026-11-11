namespace Nullean.Make.Fs

open System

/// <summary>
/// Builds a target from a block of plain F#: <c>exec { }</c> calls, <c>if</c>, <c>match</c>, loops. The block is deferred:
/// it runs only when the target runs. Created by <see cref="M:Nullean.Make.Fs.MakeBuilders.target"/>.
/// </summary>
type TargetBuilder<'TCase>(dependsOn: 'TCase list) =
    member _.Zero() = ()
    member _.Delay(f: unit -> unit) = f
    member _.Combine((), f: unit -> unit) = f ()
    member _.For(items: seq<'a>, body: 'a -> unit) = for item in items do body item
    member _.While(condition: unit -> bool, body: unit -> unit) = while condition () do body ()
    member _.TryWith(body: unit -> unit, handler: exn -> unit) = try body () with e -> handler e
    member _.TryFinally(body: unit -> unit, cleanup: unit -> unit) = try body () finally cleanup ()
    member _.Using(resource: 'r when 'r :> IDisposable, body: 'r -> unit) = use r = resource in body r
    member _.Run(body: unit -> unit) : Definition<'TCase> = FsTarget("", dependsOn, fun _ -> body ())

/// The requires and composes lists a <c>command { }</c> block collects.
[<NoComparison; NoEquality>]
type CommandSpec<'TCase> = internal { Requires: 'TCase list; Composes: 'TCase list }

/// <summary>
/// Builds a command from <c>requires</c> (gates, skipped under <c>-s</c>) and <c>composes</c> (always run):
/// <code>
/// | Release -> command {
///       requires [PristineCheck; Test defaultTest]
///       composes [Pkg Generate; Pkg PkgTarget.Validate] }
/// </code>
/// </summary>
type CommandBuilder() =
    member _.Yield(()) : CommandSpec<'TCase> = { Requires = []; Composes = [] }
    /// Targets that must succeed first. Skipped under -s.
    [<CustomOperation("requires")>]
    member _.Requires(spec: CommandSpec<'TCase>, targets: 'TCase list) = { spec with Requires = spec.Requires @ targets }
    /// Targets the command always runs, in order.
    [<CustomOperation("composes")>]
    member _.Composes(spec: CommandSpec<'TCase>, targets: 'TCase list) = { spec with Composes = spec.Composes @ targets }
    member _.Run(spec: CommandSpec<'TCase>) : Definition<'TCase> = FsCommand("", spec.Requires, spec.Composes, None)

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
