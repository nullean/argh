module Nullean.Make.Fs.Tests.MakeAppTests

open System
open System.IO
open TUnit.Core
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

[<NotInParallel>]
type MakeAppTests() =

    [<Test>]
    member _.``Target runs its dependencies first``() =
        let r = runApp standard ["build"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["clean"; "build:Release:1:Low:dry=False"], r.Log)

    [<Test>]
    member _.``Single target skips dependencies``() =
        let r = runApp standard ["build"; "-s"]
        Assert.Equal<string list>(["build:Release:1:Low:dry=False"], r.Log)

    [<Test>]
    member _.``Record payload fields bind from target flags``() =
        let r = runApp standard ["test"; "--filter"; "Unit"; "--retries"; "3"; "-s"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["test:Some \"Unit\":Some 3"], r.Log)

    [<Test>]
    member _.``Typed global options bind``() =
        let r = runApp standard ["build"; "-s"; "--config"; "Debug"; "--workers"; "4"; "--level"; "high"; "-n"]
        okExit r
        Assert.Equal<string list>(["build:Debug:4:High:dry=True"], r.Log)

    [<Test>]
    member _.``Nested DUs route through namespaces with shared case names``() =
        let r = runApp standard ["pkg"; "validate"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["pkg-validate"], r.Log)
        let r2 = runApp standard ["schema"; "validate"]
        Assert.Equal<string list>(["schema-validate"], r2.Log)

    [<Test>]
    member _.``Command runs requires then composes``() =
        let r = runApp standard ["release"]
        okExit r
        Assert.Equal<string list>(["clean"; "build:Release:1:Low:dry=False"; "test:None:None"; "pkg-generate"; "pkg-validate"], r.Log)

    [<Test>]
    member _.``Dependency with a non default payload resolves``() =
        let r = runApp standard ["focused"]
        okExit r
        Assert.Contains("test:None:None", r.Log)

    [<Test>]
    member _.``Namespace help lists the namespace targets``() =
        let r = runApp standard ["schema"; "--help"]
        Assert.Equal(0, r.ExitCode)
        Assert.Contains("Regenerate the schema", r.Stdout)
        Assert.Contains("Check the schema", r.Stdout)
        Assert.DoesNotContain("Delete artifacts", r.Stdout)

    [<Test>]
    member _.``Root help shows option types``() =
        let r = runApp standard ["--help"]
        Assert.Contains("--config <string>", r.Stdout)
        Assert.Contains("--workers <int>", r.Stdout)
        Assert.Contains("--level <level>", r.Stdout)

    [<Test>]
    member _.``Unparseable global option value exits 2 without running``() =
        let r = runApp standard ["build"; "--workers"; "lots"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("Cannot parse 'lots'", r.Stderr)
        Assert.Empty(r.Log)

    [<Test>]
    member _.``Global option without a value exits 2``() =
        let r = runApp standard ["build"; "--config"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("Missing value for '--config'.", r.Stderr)
        Assert.Empty(r.Log)

    [<Test>]
    member _.``Unknown target exits 2``() =
        let r = runApp standard ["deploy"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("Unknown target 'deploy'.", r.Stderr)

    [<Test>]
    member _.``Wrappers sync writes the F# targets, record fields as flags``() =
        let dir = Path.Combine(Path.GetTempPath(), "make-fs-wrappers-" + Guid.NewGuid().ToString("N"))
        try
            let r = runApp standard ["__wrappers"; "sync"; "--run"; "./build.fsx"; "--out-dir"; dir]
            okExit r
            Assert.Empty(r.Log)
            let mise = File.ReadAllText(Path.Combine(dir, "mise-tasks.toml"))
            Assert.Contains("[\"schema:update\"]\ndescription = \"Regenerate the schema\"", mise)
            Assert.Contains("run = \"./build.fsx test\"", mise)
            Assert.Contains("flag \"--filter <filter>\" help=\"string?\"", mise)
            Assert.Contains("flag \"-n --dry-run\" help=\"Print, do not run\"", mise)
            Assert.Contains("flag \"--workers <workers>\" help=\"Worker count\"", mise)
            Assert.Contains("pkg/validate:\n\t@$(NULLEAN_MAKE_RUN) pkg validate $(ARGS)", File.ReadAllText(Path.Combine(dir, "make.mk")))
            Assert.Contains("@schema-validate *args:", File.ReadAllText(Path.Combine(dir, "make.just")))

            let check = runApp standard ["__wrappers"; "check"; "--out-dir"; dir]
            okExit check
        finally
            if Directory.Exists dir then Directory.Delete(dir, true)

// ---- positional arguments, forwarding, --name=value, default target, hidden steps, version ----

type OneOptions =
    { [<Nullean.Argh.Argument>] Project: string option
      Verbose: bool }

type ManyOptions =
    { [<Nullean.Argh.Argument>] Files: string list
      Level: int option }

type ForwardOptions =
    { Suite: string option
      Parallelism: int option
      [<Rest>] Rest: string list }

type Tgt =
    | Build
    | Compile
    | One of OneOptions
    | Many of ManyOptions
    | Fwd of ForwardOptions

let private runTgt (configure: MakeApp<Tgt> -> unit) (args: string list) =
    let log = ResizeArray<string>()
    let app = MakeApp<Tgt>("build", None)
    let token = app.Option<string option>("--token", defaultValue = None)
    app.Bind <| function
        | Build   -> Make.target [Compile] "Build it" <| fun _ -> log.Add $"build token=%A{token.Value}"
        | Compile -> Make.step [] <| fun _ -> log.Add "compile"
        | One o   -> Make.target [] "One" <| fun _ -> log.Add $"one %A{o.Project} {o.Verbose}"
        | Many o  -> Make.target [] "Many" <| fun _ -> log.Add $"many %A{o.Files} %A{o.Level}"
        | Fwd o   -> Make.target [] "Fwd" <| fun _ -> log.Add $"fwd %A{o.Suite} %A{o.Rest}"
    configure app
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
type BinderIssueTests() =

    [<Test>]
    member _.``Argument field binds a positional``() =
        let r = runTgt ignore ["one"; "src/Foo"; "--verbose"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["one Some \"src/Foo\" True"], r.Log)

    [<Test>]
    member _.``A positional no field takes exits 2``() =
        let r = runTgt ignore ["one"; "a"; "b"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("Unexpected argument 'b'.", r.Stderr)
        Assert.Empty(r.Log)

    [<Test>]
    member _.``A target without a record rejects extra arguments``() =
        let r = runTgt ignore ["compile"; "x"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("Unexpected argument 'x'", r.Stderr)

    [<Test>]
    member _.``Variadic positional takes every remaining positional``() =
        let r = runTgt ignore ["many"; "a"; "b"; "--level"; "2"; "c"]
        Assert.Equal<string list>(["many [\"a\"; \"b\"; \"c\"] Some 2"], r.Log)

    [<Test>]
    member _.``Arguments after double dash go to the Rest field``() =
        let r = runTgt ignore ["fwd"; "--suite"; "unit"; "--"; "--treenode-filter"; "/*/*/My/*"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["fwd Some \"unit\" [\"--treenode-filter\"; \"/*/*/My/*\"]"], r.Log)

    [<Test>]
    member _.``Double dash without a Rest field exits 2``() =
        let r = runTgt ignore ["one"; "--"; "x"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("Unexpected argument 'x'", r.Stderr)

    [<Test>]
    member _.``Equals syntax works for target options``() =
        let r = runTgt ignore ["fwd"; "--suite=unit"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["fwd Some \"unit\" []"], r.Log)

    [<Test>]
    member _.``Equals syntax works for global options``() =
        let r = runTgt ignore ["--token=abc"; "build"]
        Assert.Equal(0, r.ExitCode)
        Assert.Contains("build token=Some \"abc\"", r.Log)

    [<Test>]
    member _.``Parse error names the kebab-case flag``() =
        let r = runTgt ignore ["fwd"; "--parallelism"; "x"]
        Assert.Equal(2, r.ExitCode)
        Assert.Contains("for '--parallelism'", r.Stderr)

    [<Test>]
    member _.``Version prints the entry assembly version, not a placeholder``() =
        let r = runTgt ignore ["--version"]
        Assert.Equal(0, r.ExitCode)
        Assert.Equal(false, r.Stdout.Trim() = "0.0.0")
        Assert.Equal(false, String.IsNullOrWhiteSpace r.Stdout)

    [<Test>]
    member _.``Hidden step stays out of help but runs as a dependency and by name``() =
        let help = runTgt ignore ["--help"]
        // "compile" still shows as a dependency of build, but not as a listed target.
        Assert.Equal(false, help.Stdout.Split('\n') |> Array.exists (fun l -> l.TrimStart().StartsWith("compile")))
        Assert.Contains("build", help.Stdout)
        Assert.Equal<string list>(["compile"; "build token=None"], (runTgt ignore ["build"]).Log)
        Assert.Equal<string list>(["compile"], (runTgt ignore ["compile"]).Log)

    [<Test>]
    member _.``Default target runs with no arguments``() =
        let r = runTgt (fun a -> a.Default Build) []
        Assert.Equal(0, r.ExitCode)
        Assert.Equal<string list>(["compile"; "build token=None"], r.Log)

    [<Test>]
    member _.``Default target runs when only global options are given``() =
        let r = runTgt (fun a -> a.Default Build) ["--token"; "abc"]
        Assert.Equal<string list>(["compile"; "build token=Some \"abc\""], r.Log)

    [<Test>]
    member _.``Default target is shown in help``() =
        let r = runTgt (fun a -> a.Default Build) ["--help"]
        Assert.Contains("runs build when no target is named", r.Stdout)

    [<Test>]
    member _.``Single target with no target keeps printing help``() =
        let r = runTgt (fun a -> a.Default Build) ["-s"]
        Assert.Equal(0, r.ExitCode)
        Assert.Empty(r.Log)
        Assert.Contains("Usage:", r.Stdout)

    [<Test>]
    member _.``No default keeps printing help``() =
        let r = runTgt ignore []
        Assert.Empty(r.Log)
        Assert.Contains("Usage:", r.Stdout)

    [<Test>]
    member _.``Wrappers mark list positionals and rest arguments as variadic``() =
        let dir = Path.Combine(Path.GetTempPath(), "make-fs-wrappers-" + Guid.NewGuid().ToString("N"))
        try
            let r = runTgt ignore ["__wrappers"; "sync"; "--run"; "./build.fsx"; "--only"; "mise"; "--out-dir"; dir]
            Assert.Equal(0, r.ExitCode)
            let mise = File.ReadAllText(Path.Combine(dir, "mise-tasks.toml"))
            Assert.Contains("arg \"[files]\" var=#true help=\"string...\"", mise)
            Assert.Contains("arg \"[rest]\" var=#true help=\"string... (everything after --)\"", mise)
            Assert.Contains("arg \"[project]\" help=\"string?\"", mise)
        finally
            if Directory.Exists dir then Directory.Delete(dir, true)

    [<Test>]
    member _.``Help shows an Argument record field as a positional, not a flag``() =
        let r = runTgt ignore ["one"; "--help"]
        Assert.Equal(0, r.ExitCode)
        Assert.Contains("<project>", r.Stdout)
        Assert.DoesNotContain("--project", r.Stdout)
