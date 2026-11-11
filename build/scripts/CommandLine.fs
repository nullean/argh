module CommandLine

open Argu
open Microsoft.FSharp.Reflection

type Arguments =
    | [<CliPrefix(CliPrefix.None);SubCommand>] Clean
    | [<CliPrefix(CliPrefix.None);SubCommand>] Build
    | [<CliPrefix(CliPrefix.None);SubCommand>] Test
    | [<CliPrefix(CliPrefix.None);SubCommand>] Pack
    | [<CliPrefix(CliPrefix.None);SubCommand>] Ci

    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GeneratePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] ValidatePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GenerateApiChanges
    | [<CliPrefix(CliPrefix.None);SubCommand>] UpdateSchema
    | [<CliPrefix(CliPrefix.None);SubCommand>] ValidateSchema

    | [<CliPrefix(CliPrefix.None);SubCommand>] Docs

    | [<Inherit;AltCommandLine("-s")>] SingleTarget of bool
with
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Clean -> "clean known output locations"
            | Build -> "Run build"
            | Test -> "Runs build then tests"
            | Pack -> "Runs build, then creates and validates the packages and reports public API changes"
            | Ci -> "Runs tests, checks the schema is up to date, then creates and validates the packages: exactly what CI runs"

            | SingleTarget _ -> "Runs the provided sub command without running their dependencies"

            | UpdateSchema -> "Run the schema export tool and write schema/argh-cli-schema.json"
            | ValidateSchema -> "Fail if schema/argh-cli-schema.json is out of date"
            | Docs -> "Build the documentation site and serve it locally"

            | GeneratePackages
            | ValidatePackages
            | GenerateApiChanges
                -> "Undocumented, dependent target"
    member this.Name =
        match FSharpValue.GetUnionFields(this, typeof<Arguments>) with
        | case, _ -> case.Name.ToLowerInvariant()
