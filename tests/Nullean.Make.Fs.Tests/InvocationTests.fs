module Nullean.Make.Fs.Tests.InvocationTests

open System
open System.IO
open TUnit.Core
open Nullean.Make.Fs

type VerifyOptions = { Suite: string option }

type Called =
    | Restore
    | Verify of VerifyOptions
    | Bundle
    | Shipit

let private run (args: string list) =
    let log = ResizeArray<string>()
    let app = MakeApp<Called>("build", None)
    app.Bind <| function
        | Restore  -> target [] { log.Add "restore" }
        | Verify o -> target [Restore] { log.Add $"verify:%A{o.Suite}" }
        | Bundle   -> target [Restore] { log.Add "bundle" }
        | Shipit   -> command {
                          requires [Verify { Suite = None }]
                          composes [Bundle] }
    let stderr = new StringWriter()
    let oldOut, oldErr = Console.Out, Console.Error
    Console.SetOut(new StringWriter())
    Console.SetError stderr
    try
        let exit = app.RunAsync(List.toArray args).GetAwaiter().GetResult()
        exit, List.ofSeq log, string stderr
    finally
        Console.SetOut oldOut
        Console.SetError oldErr

[<NotInParallel>]
type InvocationTests() =

    [<Test>]
    member _.``Several targets run in order and share their dependencies``() =
        let exit, log, _ = run ["bundle"; "verify"; "--suite"; "unit"]
        Assert.Equal(0, exit)
        Assert.Equal<string list>([ "restore"; "bundle"; "verify:Some \"unit\"" ], log)

    [<Test>]
    member _.``A target without a record still rejects a word that is not a target``() =
        let exit, _, stderr = run ["bundle"; "nope"]
        Assert.Equal(2, exit)
        Assert.Contains("Unexpected argument 'nope'. Target 'bundle' takes no arguments.", stderr)

    [<Test>]
    member _.``Skip leaves a target out of the plan``() =
        let exit, log, _ = run ["shipit"; "--skip"; "verify"]
        Assert.Equal(0, exit)
        Assert.Equal<string list>([ "restore"; "bundle" ], log)
