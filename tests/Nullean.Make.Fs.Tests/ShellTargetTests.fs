module Nullean.Make.Fs.Tests.ShellTargetTests

open System
open System.IO
open Xunit
open Nullean.Make.Fs
open Proc.Fs

type ShellStep =
    | Version
    | Capture
    | Loop
    | Branch
    | Folder
    | Fails
    | AfterVersion

type Result = { ExitCode: int; Log: string list; Stdout: string; Stderr: string }

let private tempDir = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)

let private runShell (args: string list) =
    let log = ResizeArray<string>()
    let app = MakeApp<ShellStep>("build", None)
    let flag = "--version"
    app.Bind <| function
        | Version -> target [] {
              sh $"dotnet --version"
              log.Add "version" }
        | Capture -> target [] {
              let! version = text $"dotnet {flag}"
              log.Add $"major:{version.Split('.').[0]}" }
        | Loop -> target [] {
              let! sdks = lines $"dotnet --list-sdks"
              for sdk in sdks |> List.truncate 1 do
                  log.Add $"sdk-line:{not (String.IsNullOrWhiteSpace sdk)}" }
        | Branch -> target [] {
              let! ok = succeeds $"dotnet definitely-not-a-dotnet-command"
              if ok then log.Add "unexpected" else log.Add "failed-without-throwing" }
        | Folder -> target [] {
              use! _ = inDir tempDir
              let! here = pwd
              log.Add $"in-temp:{Path.GetFullPath(here).TrimEnd(Path.DirectorySeparatorChar) = tempDir}" }
        | Fails -> target [] {
              sh $"dotnet definitely-not-a-dotnet-command"
              log.Add "after" }
        | AfterVersion -> target [Version] { log.Add "after-version" }
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

[<Collection("Console")>]
type ShellTargetTests() =

    [<Fact>]
    member _.``sh runs a command in a target block``() =
        let r = runShell ["version"]
        ok r
        Assert.Equal<string list>(["version"], r.Log)

    [<Fact>]
    member _.``let! text captures standard out, placeholders included``() =
        let r = runShell ["capture"]
        ok r
        Assert.Equal<string list>(["major:11"], r.Log)

    [<Fact>]
    member _.``let! lines feeds a for loop``() =
        let r = runShell ["loop"]
        ok r
        Assert.Equal<string list>(["sdk-line:True"], r.Log)

    [<Fact>]
    member _.``succeeds reports failure without throwing``() =
        let r = runShell ["branch"]
        ok r
        Assert.Equal<string list>(["failed-without-throwing"], r.Log)

    [<Fact>]
    member _.``inDir changes the directory for the rest of the block``() =
        let r = runShell ["folder"]
        ok r
        Assert.Equal<string list>(["in-temp:True"], r.Log)

    [<Fact>]
    member _.``a failing sh fails the target and stops the block``() =
        let r = runShell ["fails"]
        Assert.Equal(1, r.ExitCode)
        Assert.Contains("[make] Target 'fails' failed", r.Stderr)
        Assert.Empty(r.Log)

    [<Fact>]
    member _.``shell blocks are deferred until their target runs``() =
        let r = runShell ["after-version"; "-s"]
        ok r
        Assert.Equal<string list>(["after-version"], r.Log)
        let help = runShell ["--help"]
        Assert.Empty(help.Log)
