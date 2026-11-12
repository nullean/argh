module Nullean.Make.Fs.Tests.GraphTests

open System
open System.IO
open TUnit.Core
open Nullean.Make.Fs

type ProbeOptions = { Filter: string option }

type Drawn =
    | Wipe
    | Compose
    | Probe of ProbeOptions
    | Ship

let private run (args: string list) =
    let log = ResizeArray<string>()
    let app = MakeApp<Drawn>("build", None)
    app.Bind <| function
        | Wipe    -> target [] { log.Add "wipe" }
        | Compose -> target [Wipe] { log.Add "compose" }
        | Probe o -> target [Compose] { log.Add $"probe:%A{o.Filter}" }
        | Ship    -> command {
                         requires [Probe { Filter = Some "Unit" }]
                         composes [Wipe] }
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
type GraphTests() =

    [<Test>]
    member _.``Graph draws the plan and runs nothing``() =
        let exit, log, stdout = run ["ship"; "--graph"]
        Assert.Equal(0, exit)
        Assert.Equal<string list>([], log)
        Assert.Contains("probe (filter: Unit)", stdout)
        Assert.Contains("Runs: wipe → compose → probe (filter: Unit)", stdout)

    [<Test>]
    member _.``Graph shows a gate that single target skips``() =
        let _, _, stdout = run ["ship"; "-s"; "--graph"]
        Assert.Contains("probe (filter: Unit)  skipped: -s", stdout)
        Assert.Contains("Runs: wipe", stdout)

    [<Test>]
    member _.``Graph without a target draws the whole build``() =
        let exit, log, stdout = run ["--graph"]
        Assert.Equal(0, exit)
        Assert.Equal<string list>([], log)
        // Everything else is a dependency of ship, so ship is the one root.
        Assert.Equal(true, stdout.StartsWith "ship")

    [<Test>]
    member _.``Graph mermaid prints a flowchart``() =
        let _, _, stdout = run ["probe"; "--graph"; "mermaid"]
        Assert.Contains("flowchart TD", stdout)
        Assert.Contains("n0 --> n1", stdout)
