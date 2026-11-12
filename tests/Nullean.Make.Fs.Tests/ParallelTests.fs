module Nullean.Make.Fs.Tests.ParallelTests

open System
open System.IO
open System.Threading
open System.Collections.Concurrent
open TUnit.Core
open Nullean.Make.Fs

type Side =
    | Left
    | Right
    | After
    | Both

let private run (args: string list) =
    let log = ConcurrentQueue<string>()
    use both = new CountdownEvent(2)
    // Left and Right each wait for the other to start: side by side they meet, one after the other they do not.
    let meet name =
        both.Signal() |> ignore
        log.Enqueue(if both.Wait(TimeSpan.FromSeconds 3.) then $"{name}:met" else $"{name}:alone")
    let app = MakeApp<Side>("build", None)
    app.Bind <| function
        | Left  -> Make.target [] "" (fun _ -> meet "left")
        | Right -> Make.target [] "" (fun _ -> meet "right")
        | After -> Make.target [] "" (fun _ -> log.Enqueue "after")
        | Both  -> command {
                       concurrently [Left; Right]
                       composes [After] }
    let stdout = new StringWriter()
    let oldOut, oldErr = Console.Out, Console.Error
    Console.SetOut stdout
    Console.SetError(new StringWriter())
    try
        let exit = app.RunAsync(List.toArray args).GetAwaiter().GetResult()
        exit, List.ofSeq log, string stdout
    finally
        Console.SetOut oldOut
        Console.SetError oldErr

[<NotInParallel>]
type ParallelTests() =

    [<Test>]
    member _.``A concurrently stage runs its targets side by side``() =
        let exit, log, _ = run ["both"; "-j"; "2"]
        Assert.Equal(0, exit)
        Assert.Equal<string list>([ "left:met"; "right:met" ], log |> List.take 2 |> List.sort)
        Assert.Equal("after", List.last log)

    [<Test>]
    member _.``One job runs a parallel stage one target at a time``() =
        let exit, log, _ = run ["both"; "-j"; "1"]
        Assert.Equal(0, exit)
        Assert.Equal<string list>([ "left:alone"; "right:met"; "after" ], log)

    [<Test>]
    member _.``Graph draws the parallel stage``() =
        let _, _, stdout = run ["both"; "--graph"]
        Assert.Contains("parallel", stdout)
