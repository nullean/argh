namespace Nullean.Make.Fs

open System

[<AutoOpen>]
module MakeScriptHelpers =
    /// Abort the build with a message and exit code 1.
    /// Prefer this over raising MakeException directly — keeps Nullean.Make out of script references.
    let failBuild (message: string) : 'a =
        raise (Nullean.Make.MakeException(message))

/// <summary>
/// Marks a record field of type <c>'T list</c> as the receiver of everything after a <c>--</c> on the command line,
/// so a target can forward arguments it does not know about: <c>build test -- --treenode-filter "/*/*/MyTests/*"</c>.
/// </summary>
[<AttributeUsage(AttributeTargets.Property ||| AttributeTargets.Field ||| AttributeTargets.Parameter)>]
type RestAttribute() =
    inherit Attribute()

/// Mutable handle to a global option. Populated during argv parsing; read by target bodies via FsContext.
type OptionRef<'T>(long: string, short: string option, desc: string option, defaultValue: 'T, parser: string -> 'T) =
    let mutable _value = defaultValue
    member _.Long = long
    member _.Short = short
    member _.Description = desc
    member _.DefaultValue = defaultValue
    /// Current parsed value — valid after MakeApp.RunAsync starts argv extraction.
    member _.Value = _value
    member internal _.Set(raw: string) = _value <- parser raw
    member internal _.Reset() = _value <- defaultValue

/// Passed to target bodies. Provides typed reads of global option values.
type FsContext internal () =
    /// Returns the current value of a global option.
    member _.Get(optRef: OptionRef<'T>) : 'T = optRef.Value
    /// Returns true if the flag was passed on the command line.
    member _.IsSet(optRef: OptionRef<bool>) : bool = optRef.Value

/// Returned by app.Bind for each DU case.
[<NoComparison; NoEquality>]
type Definition<'TCase> =
    internal
    | FsTarget    of desc: string * deps: 'TCase list * body: (FsContext -> unit) * hidden: bool
    /// stages: the composed targets in declared order; true marks a stage whose targets run side by side
    | FsCommand   of desc: string * requires: 'TCase list * stages: (bool * 'TCase list) list * body: (FsContext -> unit) option
    | FsNamespace of segment: string * desc: string option

/// Helpers for building Definition values inside app.Bind.
module Make =

    /// Defines an atomic target. Pass "" for desc to use the kebab-case case name.
    let target (deps: 'TCase list) (desc: string) (body: FsContext -> 'r) : Definition<'TCase> =
        FsTarget(desc, deps, (fun ctx -> body ctx |> ignore), false)

    /// Like `target`, but left out of help. It still runs when named and when another target depends on it.
    let step (deps: 'TCase list) (body: FsContext -> 'r) : Definition<'TCase> =
        FsTarget("", deps, (fun ctx -> body ctx |> ignore), true)

    /// Marks a DU case as a CLI namespace segment. Not needed when using nested sub-DUs.
    let ns (segment: string) (desc: string option) : Definition<'TCase> =
        FsNamespace(segment, desc)

    /// Defines a command that composes other targets/commands.
    /// `requires` entries are skipped under -s; `composes` entries always run.
    /// Description is auto-generated from the requires/composes graph in help output.
    let command (requires: 'TCase list) (composes: 'TCase list) : Definition<'TCase> =
        FsCommand("", requires, [false, composes], None)

    /// Like `command` but with a trailing body that runs after all `composes` entries.
    let composer (requires: 'TCase list) (composes: 'TCase list) (body: FsContext -> 'r) : Definition<'TCase> =
        FsCommand("", requires, [false, composes], Some (fun ctx -> body ctx |> ignore))
