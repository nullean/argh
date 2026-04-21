module CommandLine

open Argu
open Microsoft.FSharp.Reflection

type Arguments =
    | [<CliPrefix(CliPrefix.None);SubCommand>] Clean
    | [<CliPrefix(CliPrefix.None);SubCommand>] Build
    | [<CliPrefix(CliPrefix.None);SubCommand>] Test

    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] PristineCheck
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GeneratePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] ValidatePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GenerateReleaseNotes
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GenerateApiChanges
    | [<CliPrefix(CliPrefix.None);SubCommand>] UpdateSchema
    | [<CliPrefix(CliPrefix.None);SubCommand>] ValidateSchema

    | [<CliPrefix(CliPrefix.None);SubCommand>] Release

    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] CreateReleaseOnGithub
    | [<CliPrefix(CliPrefix.None);SubCommand>] Publish

    | [<Inherit;AltCommandLine("-s")>] SingleTarget of bool
    | [<Inherit>] Token of string
    | [<Inherit;AltCommandLine("-c")>] CleanCheckout of bool
with
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Clean -> "clean known output locations"
            | Build -> "Run build"
            | Test -> "Runs build then tests"
            | Release -> "runs build, tests, and create and validates the packages shy of publishing them"
            | Publish -> "Runs the full release"

            | SingleTarget _ -> "Runs the provided sub command without running their dependencies"
            | Token _ -> "Token to be used to authenticate with github"
            | CleanCheckout _ -> "Skip the clean checkout check that guards the release/publish targets"

            | UpdateSchema -> "Run the schema export tool and write schema/argh-cli-schema.json"
            | ValidateSchema -> "Fail if schema/argh-cli-schema.json is out of date"

            | PristineCheck
            | GeneratePackages
            | ValidatePackages
            | GenerateReleaseNotes
            | GenerateApiChanges
            | CreateReleaseOnGithub
                -> "Undocumented, dependent target"
    member this.Name =
        match FSharpValue.GetUnionFields(this, typeof<Arguments>) with
        | case, _ -> case.Name.ToLowerInvariant()
