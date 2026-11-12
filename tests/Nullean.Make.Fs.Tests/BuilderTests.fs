module Nullean.Make.Fs.Tests.BuilderTests

open System
open System.IO
open TUnit.Core
open Nullean.Make.Fs
open Proc.Fs

type CheckOptions = { Filter: string option }

type PkgStep =
    /// Pack every project
    | Generate
    /// Validate the packages
    | Validate

type Step =
    /// Delete build output
    | Clean
    /// Compile the solution
    /// in Release
    | Compile
    /// Run the tests
    | Check of CheckOptions
    | Pkg of PkgStep
    /// Build, check and pack
    | Ship
    | Undocumented
    | RunsDotnet
    | FailsDotnet
    | CaptureInTarget
    | CaptureInCommand

type Result = { ExitCode: int; Log: string list; Stdout: string; Stderr: string }

let private runSteps (args: string list) =
    let log = ResizeArray<string>()
    let app = MakeApp<Step>("build", None)
    app.Bind <| function
        | Clean        -> target [] { log.Add "clean" }
        | Compile      -> target [Clean] { log.Add "compile" }
        | Check o      -> target [Compile] {
                              match o.Filter with
                              | Some f -> log.Add $"check:{f}"
                              | None -> log.Add "check:all"
                              for i in 1 .. 2 do log.Add $"pass{i}" }
        | Pkg Generate -> target [] { log.Add "pkg-generate" }
        | Pkg Validate -> target [] { log.Add "pkg-validate" }
        | Ship         -> command {
                              requires [Check { Filter = None }]
                              composes [Pkg Generate; Pkg Validate] }
        | Undocumented -> target [] { log.Add "undocumented" }
        | RunsDotnet   -> target [] {
                              exec { run "dotnet" "--version" }
                              log.Add "ran" }
        | FailsDotnet  -> target [] {
                              exec { run "dotnet" "definitely-not-a-dotnet-command" }
                              log.Add "after" }
        | CaptureInTarget -> target [] {
                              let helpOutput = exec {
                                  binary "dotnet"
                                  arguments "--help"
                                  output
                              }
                              log.Add $"target-exit:{helpOutput.ExitCode}"
                              if helpOutput.ConsoleOut |> Seq.exists (fun l -> l.Line.Contains "build") then
                                  log.Add "target-saw-build" }
        | CaptureInCommand -> command {
                                  requires [Clean]
                                  composes [Pkg Generate] }
                              |> executes {
                                  let helpOutput = exec {
                                      binary "dotnet"
                                      arguments "--help"
                                      output
                                  }
                                  log.Add $"command-exit:{helpOutput.ExitCode}" }
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

let private ok (r: Result) =
    if r.ExitCode <> 0 then failwithf "expected exit 0, got %d. stderr: %s" r.ExitCode r.Stderr

[<NotInParallel>]
type BuilderTests() =

    [<Test>]
    member _.``target block runs after its dependencies and only when run``() =
        let r = runSteps ["compile"]
        ok r
        Assert.Equal<string list>(["clean"; "compile"], r.Log)

    [<Test>]
    member _.``target block can branch and loop on the case payload``() =
        let r = runSteps ["check"; "--filter"; "Unit"; "-s"]
        ok r
        Assert.Equal<string list>(["check:Unit"; "pass1"; "pass2"], r.Log)

    [<Test>]
    member _.``command block runs requires then composes``() =
        let r = runSteps ["ship"]
        ok r
        Assert.Equal<string list>(["clean"; "compile"; "check:all"; "pass1"; "pass2"; "pkg-generate"; "pkg-validate"], r.Log)

    [<Test>]
    member _.``command block skips requires under -s``() =
        let r = runSteps ["ship"; "-s"]
        ok r
        Assert.Equal<string list>(["pkg-generate"; "pkg-validate"], r.Log)

    [<Test>]
    member _.``Descriptions come from DU case doc comments``() =
        let r = runSteps ["--help"]
        Assert.Contains("Delete build output", r.Stdout)
        // Multi-line docs collapse to one line.
        Assert.Contains("Compile the solution in Release", r.Stdout)
        Assert.Contains("Run the tests", r.Stdout)
        Assert.Contains("Build, check and pack", r.Stdout)

    [<Test>]
    member _.``Namespace sub cases take their own doc comments``() =
        let r = runSteps ["pkg"; "--help"]
        Assert.Contains("Pack every project", r.Stdout)
        Assert.Contains("Validate the packages", r.Stdout)

    [<Test>]
    member _.``exec inside a target runs the command``() =
        let r = runSteps ["runs-dotnet"]
        ok r
        Assert.Equal<string list>(["ran"], r.Log)

    [<Test>]
    member _.``exec output can be captured in a target block``() =
        let r = runSteps ["capture-in-target"]
        ok r
        Assert.Equal<string list>(["target-exit:0"; "target-saw-build"], r.Log)

    [<Test>]
    member _.``command executes body runs after its composes``() =
        let r = runSteps ["capture-in-command"]
        ok r
        Assert.Equal<string list>(["clean"; "pkg-generate"; "command-exit:0"], r.Log)

    [<Test>]
    member _.``command executes body is deferred``() =
        let r = runSteps ["--help"]
        ok r
        Assert.Empty(r.Log)

    [<Test>]
    member _.``a failing exec fails the target``() =
        let r = runSteps ["fails-dotnet"]
        Assert.Equal(1, r.ExitCode)
        Assert.Contains("[make] Target 'fails-dotnet' failed", r.Stderr)
        Assert.Empty(r.Log)
