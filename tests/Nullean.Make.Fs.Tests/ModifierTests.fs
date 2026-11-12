module Nullean.Make.Fs.Tests.ModifierTests

open System
open System.IO
open TUnit.Core
open Nullean.Make.Fs

type Modified =
    | Lint
    | Package
    | Ci
    | StartDb
    | StopDb
    | Integration
    | Push
    | Optional

let private variable = "NULLEAN_MAKE_FS_TEST_PUSH_KEY"

let private run (args: string list) =
    let log = ResizeArray<string>()
    let app = MakeApp<Modified>("build", None)
    let skip = app.Flag("--skip-optional")
    app.Bind <| function
        | Lint        -> target [] { log.Add "lint" }
        | Package     -> target [] { log.Add "package" } |> after [Lint]
        | Ci          -> command { concurrently [Package; Lint] }
        | StartDb     -> target [] { log.Add "start-db" } |> cleanup [StopDb]
        | StopDb      -> target [] { log.Add "stop-db" }
        | Integration -> target [StartDb] { failBuild "integration failed" }
        | Push        -> target [] { log.Add "push" } |> needsEnv [variable] |> describe "Push the packages"
        | Optional    -> target [Lint] { log.Add "optional" } |> onlyWhen (fun () -> not skip.Value)
    let stdout, stderr = new StringWriter(), new StringWriter()
    let oldOut, oldErr = Console.Out, Console.Error
    Console.SetOut stdout
    Console.SetError stderr
    try
        let exit = app.RunAsync(List.toArray args).GetAwaiter().GetResult()
        exit, List.ofSeq log, string stdout, string stderr
    finally
        Console.SetOut oldOut
        Console.SetError oldErr

[<NotInParallel>]
type ModifierTests() =

    [<Test>]
    member _.``onlyWhen skips the body but not the dependencies``() =
        let exit, log, _, _ = run ["optional"; "--skip-optional"]
        Assert.Equal(0, exit)
        Assert.Equal<string list>([ "lint" ], log)

    [<Test>]
    member _.``after orders a target after one in the same run``() =
        let _, log, _, _ = run ["ci"; "-j"; "4"]
        Assert.Equal<string list>([ "lint"; "package" ], log)

    [<Test>]
    member _.``after does not add the target to the run``() =
        let _, log, _, _ = run ["package"]
        Assert.Equal<string list>([ "package" ], log)

    [<Test>]
    member _.``cleanup runs after a failure``() =
        let exit, log, _, _ = run ["integration"]
        Assert.Equal(1, exit)
        Assert.Equal<string list>([ "start-db"; "stop-db" ], log)

    [<Test>]
    member _.``needsEnv stops the run before anything starts``() =
        Environment.SetEnvironmentVariable(variable, null)
        let exit, log, _, stderr = run ["push"]
        Assert.Equal(1, exit)
        Assert.Equal<string list>([], log)
        Assert.Contains(variable, stderr)

    [<Test>]
    member _.``describe sets the description help shows``() =
        let _, _, stdout, _ = run ["--help"]
        Assert.Contains("Push the packages", stdout)
