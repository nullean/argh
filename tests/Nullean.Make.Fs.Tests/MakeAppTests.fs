module Nullean.Make.Fs.Tests.MakeAppTests

open System
open System.IO
open Xunit
open Nullean.Make.Fs

type Level = Low = 0 | High = 1

type SchemaTarget = Update | Validate
type PkgTarget    = Generate | Validate

type TestOptions = { Filter: string option; Retries: int option }

type Target =
    | Schema of SchemaTarget
    | Pkg    of PkgTarget
    | Clean
    | Build
    | Test of TestOptions
    | Release
    | Focused

[<CollectionDefinition("Console", DisableParallelization = true)>]
type ConsoleCollection() = class end

type Run = { ExitCode: int; Log: string list; Stdout: string; Stderr: string }

let private runApp (configure: MakeApp<Target> -> ResizeArray<string> -> unit) (args: string list) =
    let log = ResizeArray<string>()
    let app = MakeApp<Target>("build", Some "Test build script")
    configure app log
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

/// The build script the tests share: bodies append to the log.
let private standard (app: MakeApp<Target>) (log: ResizeArray<string>) =
    let config  = app.Option<string>("--config", desc = "Build configuration", defaultValue = "Release")
    let workers = app.Option<int>("--workers", desc = "Worker count", defaultValue = 1)
    let level   = app.Option<Level>("--level", desc = "Level", defaultValue = Level.Low)
    let dryRun  = app.Flag("--dry-run", short = "-n", desc = "Print, do not run")
    app.Bind <| function
        | Clean   -> Make.target [] "Delete artifacts" <| fun _ -> log.Add "clean"
        | Build   -> Make.target [Clean] "" <| fun ctx ->
                         log.Add $"build:{ctx.Get config}:{ctx.Get workers}:{ctx.Get level}:dry={ctx.IsSet dryRun}"
        | Test o  -> Make.target [Build] "Run tests" <| fun _ ->
                         log.Add $"test:%A{o.Filter}:%A{o.Retries}"
        | Schema Update                -> Make.target [] "Regenerate the schema" <| fun _ -> log.Add "schema-update"
        | Schema SchemaTarget.Validate -> Make.target [] "Check the schema" <| fun _ -> log.Add "schema-validate"
        | Pkg Generate                 -> Make.target [] "" <| fun _ -> log.Add "pkg-generate"
        | Pkg PkgTarget.Validate       -> Make.target [] "" <| fun _ -> log.Add "pkg-validate"
        | Release -> Make.command [Test { Filter = None; Retries = None }] [Pkg Generate; Pkg PkgTarget.Validate]
        // A dependency with a non-default payload must still resolve to the Test target.
        | Focused -> Make.command [Test { Filter = Some "Unit"; Retries = Some 2 }] []

let private okExit (r: Run) =
    if r.ExitCode <> 0 then failwithf "expected exit 0, got %d. stderr: %s" r.ExitCode r.Stderr

[<Collection("Console")>]
type MakeAppTests() =

    [<Fact>]
    member _.``Target runs its dependencies first``() =
        let r = runApp standard ["build"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["clean"; "build:Release:1:Low:dry=False"], r.Log)

    [<Fact>]
    member _.``Single target skips dependencies``() =
        let r = runApp standard ["build"; "-s"]
        Assert.Equal<string list>(["build:Release:1:Low:dry=False"], r.Log)

    [<Fact>]
    member _.``Record payload fields bind from target flags``() =
        let r = runApp standard ["test"; "--filter"; "Unit"; "--retries"; "3"; "-s"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["test:Some \"Unit\":Some 3"], r.Log)

    [<Fact>]
    member _.``Typed global options bind``() =
        let r = runApp standard ["build"; "-s"; "--config"; "Debug"; "--workers"; "4"; "--level"; "high"; "-n"]
        okExit r
        Assert.Equal<string list>(["build:Debug:4:High:dry=True"], r.Log)

    [<Fact>]
    member _.``Nested DUs route through namespaces with shared case names``() =
        let r = runApp standard ["pkg"; "validate"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["pkg-validate"], r.Log)
        let r2 = runApp standard ["schema"; "validate"]
        Assert.Equal<string list>(["schema-validate"], r2.Log)

    [<Fact>]
    member _.``Command runs requires then composes``() =
        let r = runApp standard ["release"]
        okExit r
        Assert.Equal<string list>(["clean"; "build:Release:1:Low:dry=False"; "test:None:None"; "pkg-generate"; "pkg-validate"], r.Log)

    [<Fact>]
    member _.``Dependency with a non default payload resolves``() =
        let r = runApp standard ["focused"]
        okExit r
        Assert.Contains("test:None:None", r.Log)

    [<Fact>]
    member _.``Namespace help lists the namespace targets``() =
        let r = runApp standard ["schema"; "--help"]
        Assert.Equal(0, r.ExitCode)
        Assert.Contains("Regenerate the schema", r.Stdout)
        Assert.Contains("Check the schema", r.Stdout)
        Assert.DoesNotContain("Delete artifacts", r.Stdout)

    [<Fact>]
    member _.``Root help shows option types``() =
        let r = runApp standard ["--help"]
        Assert.Contains("--config <string>", r.Stdout)
        Assert.Contains("--workers <int>", r.Stdout)
        Assert.Contains("--level <level>", r.Stdout)

    [<Fact>]
    member _.``Unparseable global option value exits 2 without running``() =
        let r = runApp standard ["build"; "--workers"; "lots"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("Cannot parse 'lots'", r.Stderr)
        Assert.Empty(r.Log)

    [<Fact>]
    member _.``Global option without a value exits 2``() =
        let r = runApp standard ["build"; "--config"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("Missing value for '--config'.", r.Stderr)
        Assert.Empty(r.Log)

    [<Fact>]
    member _.``Unknown target exits 2``() =
        let r = runApp standard ["deploy"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("Unknown target 'deploy'.", r.Stderr)
