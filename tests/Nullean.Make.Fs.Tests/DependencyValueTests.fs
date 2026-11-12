module Nullean.Make.Fs.Tests.DependencyValueTests

open System
open System.IO
open TUnit.Core
open Nullean.Make.Fs

type SuiteOptions = { Filter: string option }

type Valued =
    | Suite of SuiteOptions
    | Gate of SuiteOptions
    | Split
    | Twice

type private Run = { ExitCode: int; Log: string list; Stdout: string; Stderr: string }

let private run (args: string list) =
    let log = ResizeArray<string>()
    let app = MakeApp<Valued>("build", None)
    app.Bind <| function
        | Suite o -> target [] { log.Add $"suite:%A{o.Filter}" }
        // Gate takes the same --filter as Suite; its dependency on Suite names no filter.
        | Gate o  -> target [Suite { Filter = None }] { log.Add $"gate:%A{o.Filter}" }
        | Split   -> command { composes [Suite { Filter = Some "Unit" }; Suite { Filter = Some "Integration" }] }
        | Twice   -> command { composes [Suite { Filter = Some "Unit" }; Suite { Filter = Some "Unit" }] }
    let stdout, stderr = new StringWriter(), new StringWriter()
    let oldOut, oldErr = Console.Out, Console.Error
    Console.SetOut stdout
    Console.SetError stderr
    try
        let exit = app.RunAsync(List.toArray args).GetAwaiter().GetResult()
        { ExitCode = exit; Log = List.ofSeq log; Stdout = string stdout; Stderr = string stderr }
    finally
        Console.SetOut oldOut
        Console.SetError oldErr

[<NotInParallel>]
type DependencyValueTests() =

    [<Test>]
    member _.``Two references with different values run the target once each``() =
        let r = run ["split"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>([ "suite:Some \"Unit\""; "suite:Some \"Integration\"" ], r.Log)

    [<Test>]
    member _.``Two references with equal values run the target once``() =
        let r = run ["twice"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>([ "suite:Some \"Unit\"" ], r.Log)

    [<Test>]
    member _.``The named target's arguments do not reach its dependencies``() =
        let r = run ["gate"; "--filter"; "Smoke"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>([ "suite:None"; "gate:Some \"Smoke\"" ], r.Log)

    [<Test>]
    member _.``The log names a dependency by its value``() =
        let r = run ["split"]
        Assert.Contains("suite (filter: Unit)", r.Stdout)
        Assert.Contains("suite (filter: Integration)", r.Stdout)
