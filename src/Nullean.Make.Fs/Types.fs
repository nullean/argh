namespace Nullean.Make.Fs

open System

/// Re-exported so build scripts only need `open Nullean.Make.Fs`.
type MakeException = Nullean.Make.MakeException

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
    | FsTarget    of desc: string * deps: 'TCase list * body: (FsContext -> unit)
    | FsCommand   of desc: string * requires: 'TCase list * composes: 'TCase list * body: (FsContext -> unit) option
    | FsNamespace of segment: string * desc: string option

/// Helpers for building Definition values inside app.Bind.
module Make =

    /// Defines an atomic target with an optional dependency list and a body.
    /// The body receives an FsContext for reading global options; use `_` to ignore it.
    let target (desc: string) (deps: 'TCase list) (body: FsContext -> 'r) : Definition<'TCase> =
        FsTarget(desc, deps, fun ctx -> body ctx |> ignore)

    /// Marks a DU case as a CLI namespace segment.
    let ns (segment: string) (desc: string option) : Definition<'TCase> =
        FsNamespace(segment, desc)

    /// Defines a command that composes other targets/commands.
    /// `requires` entries are skipped under -s; `composes` entries always run.
    let command (desc: string) (requires: 'TCase list) (composes: 'TCase list) : Definition<'TCase> =
        FsCommand(desc, requires, composes, None)

    /// Like `command` but with a trailing body that runs after all `composes` entries.
    let composer (desc: string) (requires: 'TCase list) (composes: 'TCase list) (body: FsContext -> 'r) : Definition<'TCase> =
        FsCommand(desc, requires, composes, Some (fun ctx -> body ctx |> ignore))
