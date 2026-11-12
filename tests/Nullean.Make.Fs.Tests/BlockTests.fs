module Nullean.Make.Fs.Tests.BlockTests

open System
open System.IO
open TUnit.Core
open Nullean.Make.Fs
open Proc.Fs

type Blocks =
    | Hello
    | Gated
    | Plain
    | Shipit

let private variable = "NULLEAN_MAKE_FS_BLOCK_KEY"

let private run (args: string list) =
    let log = ResizeArray<string>()
    let app = MakeApp<Blocks>("build", None)
    app.Bind <| function
        | Hello -> target [] {
                       describe "Say hello"
                       log.Add "hello" }
        | Gated -> target [Plain] {
                       describe "Needs a key"
                       needsEnv [variable]
                       let! version = text $"dotnet --version"
                       log.Add $"gated:{version.Split('.').[0]}" }
        | Plain -> target [] { log.Add "plain" }
        | Shipit -> command {
                        describe "Ship it"
                        composes [Hello] }
                    |> executes {
                           let! version = text $"dotnet --version"
                           log.Add $"shipit:{version.Split('.').[0]}" }
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
type BlockTests() =

    [<Test>]
    member _.``describe inside a target block sets the description``() =
        let _, log, stdout, _ = run ["--help"]
        Assert.Contains("Say hello", stdout)
        Assert.Contains("Needs a key", stdout)
        Assert.Contains("Ship it", stdout)
        // Reading the graph runs no block.
        Assert.Equal<string list>([], log)

    [<Test>]
    member _.``a block with modifier lines still runs its body``() =
        let exit, log, _, _ = run ["hello"]
        Assert.Equal(0, exit)
        Assert.Equal<string list>([ "hello" ], log)

    [<Test>]
    member _.``needsEnv inside a block stops the run before anything starts``() =
        Environment.SetEnvironmentVariable(variable, null)
        let exit, log, _, stderr = run ["gated"]
        Assert.Equal(1, exit)
        Assert.Equal<string list>([], log)
        Assert.Contains(variable, stderr)

    [<Test>]
    member _.``a block with modifiers takes let! captures``() =
        Environment.SetEnvironmentVariable(variable, "set")
        try
            let exit, log, _, _ = run ["gated"]
            Assert.Equal(0, exit)
            Assert.Equal<string list>([ "plain"; "gated:11" ], log)
        finally Environment.SetEnvironmentVariable(variable, null)

    [<Test>]
    member _.``executes gives a command a shell block body that runs last``() =
        let exit, log, _, _ = run ["shipit"]
        Assert.Equal(0, exit)
        Assert.Equal<string list>([ "hello"; "shipit:11" ], log)
