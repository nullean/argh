#!/usr/bin/env -S dotnet fsi --
// Local references — all transitive deps are co-located in the Nullean.Make.Fs output folder.
// Build first with: dotnet build -c Release
// Once Nullean.Make.Fs ships on NuGet, replace with:
//   #r "nuget: Nullean.Make.Fs, <version>"
#I ".artifacts/bin/Nullean.Make.Fs/release"
#r "Nullean.Make.Fs.dll"
#r "nuget: Proc.Fs, 0.14.0"

// F# build pipeline for nullean/argh using Nullean.Make.Fs.
//
// Run via: dotnet fsi build.fsx -- <target>
//
// The DUs encode structure statically:
//   - Sub-DU payload on a case   → namespace (Schema of SchemaTarget, Pkg of PkgTarget)
//   - Record payload on a case   → target with typed CLI args (Test of TestOptions)
//   - No payload on a case       → plain target or command

open System
open System.IO
open Nullean.Make.Fs   // MakeApp<'T>, Make module, FsContext, failBuild
open Proc.Fs

// ── constants ─────────────────────────────────────────────────────────────────

let [<Literal>] Repository    = "nullean/argh"
let [<Literal>] MainTfm       = "netstandard2.0"
let [<Literal>] SignKey        = "96c599bbe3e70f5d"
let [<Literal>] IncludeGitHash = true

let output () = DirectoryInfo(Path.Combine("build", "output"))

let outputPath () =
    Path.GetRelativePath(Directory.GetCurrentDirectory(), output().FullName)

let schemaToolBin () =
    let name = "Nullean.Argh.SchemaExport"
    if Environment.OSVersion.Platform = PlatformID.Win32NT then
        $".artifacts/bin/%s{name}/release/%s{name}.exe"
    else
        $".artifacts/bin/%s{name}/release/%s{name}"

// ── version helpers (lazy, computed once) ─────────────────────────────────────

let restoreTools =
    lazy (exec { run "dotnet" ["tool"; "restore"] })

let currentVersion =
    lazy (
        restoreTools.Value
        let r = exec { binary "dotnet"; arguments ["minver"; "-p"; "canary.0"; "-m"; "0.1"]; output }
        r.ConsoleOut |> Seq.find (fun l -> not (l.Line.StartsWith("MinVer:"))) |> fun l -> l.Line
    )

let currentVersionInformational =
    lazy (
        if IncludeGitHash then
            let r = exec { binary "git"; arguments ["rev-parse"; "--short"; "HEAD"]; output }
            $"%s{currentVersion.Value}+%s{r.ConsoleOut |> Seq.head |> _.Line.Trim()}"
        else
            currentVersion.Value
    )

let packageIdFromFile (path: string) =
    Path.GetFileNameWithoutExtension(path).Replace("." + currentVersion.Value, "")

// ── namespace sub-DUs ─────────────────────────────────────────────────────────

type SchemaTarget = Update | Validate
type PkgTarget    = Generate | Validate

// ── target / command DU ───────────────────────────────────────────────────────

type Target =
    // namespaces — payload being a union encodes the hierarchy
    | Schema of SchemaTarget
    | Pkg    of PkgTarget
    // atomic targets
    | Clean
    | Build
    | PristineCheck
    | Test of TestOptions
    | GenerateReleaseNotes
    | GenerateApiChanges
    | CreateReleaseOnGithub
    // commands
    | Release
    | Publish

and TestOptions = { Filter: string option }

let defaultTest = { Filter = None }

// ── global options ────────────────────────────────────────────────────────────

let app = MakeApp<Target>(fsi.CommandLineArgs[0], Some "Build pipeline for nullean/argh")

let cleanCheckout = app.Flag("--clean-checkout", short = "-c", desc = "Skip the clean-checkout guard")
let token         = app.Option<string option>("--token", desc = "GitHub token for release/publish", defaultValue = None)

// ── single exhaustive binding ─────────────────────────────────────────────────

app.Bind <| function

    // ── schema namespace ───────────────────────────────────────────────────
    | Schema Update ->
        Make.target [] "" <| fun _ ->
            exec { run "dotnet" ["build"; "-c"; "Release"; "tools/Nullean.Argh.SchemaExport"] }
            if not (Directory.Exists "schema") then Directory.CreateDirectory "schema" |> ignore
            exec { run (schemaToolBin()) ["--out"; "schema/argh-cli-schema.json"] }

    | Schema SchemaTarget.Validate ->
        Make.target [] "Fail if schema/argh-cli-schema.json is out of date" <| fun _ ->
            exec { run "dotnet" ["build"; "-c"; "Release"; "tools/Nullean.Argh.SchemaExport"] }
            let tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json")
            try
                exec { run (schemaToolBin()) ["--out"; tempPath] }
                let generated = File.ReadAllText(tempPath).TrimEnd()
                let existing  = File.ReadAllText("schema/argh-cli-schema.json").TrimEnd()
                if generated <> existing then
                    failBuild "schema/argh-cli-schema.json is out of date. Run: dotnet fsi build.fsx -- schema update"
            finally
                if File.Exists tempPath then File.Delete tempPath

    // ── pkg namespace ──────────────────────────────────────────────────────
    | Pkg Generate ->
        Make.target [] "" <| fun _ ->
            let out = output ()
            if out.Exists then out.Delete(true)
            exec { run "dotnet" ["pack"; "-c"; "Release"; "-o"; outputPath()] }

    | Pkg PkgTarget.Validate ->
        Make.target [] "" <| fun _ ->
            let baseArgs = [ "-v"; currentVersionInformational.Value; "-k"; SignKey; "-t"; outputPath() ]
            output().GetFiles("*.nupkg")
            |> Seq.sortByDescending _.CreationTimeUtc
            |> Seq.map  (fun f -> Path.GetRelativePath(Directory.GetCurrentDirectory(), f.FullName))
            |> Seq.filter (fun p -> packageIdFromFile p <> "Nullean.Argh")
            |> Seq.iter (fun p -> exec { run "dotnet" (["nupkg-validator"; p] @ baseArgs) })

    // ── clean ──────────────────────────────────────────────────────────────
    | Clean ->
        Make.target [] "clean ephemeral output files" <| fun _ ->
            let out = output ()
            if out.Exists then out.Delete(true)
            exec { run "dotnet" ["clean"] }

    // ── build ──────────────────────────────────────────────────────────────
    | Build ->
        Make.target [Clean] "build the solution" <| fun _ ->
            exec { run "dotnet" ["build"; "-c"; "Release"] }

    // ── pristine-check ─────────────────────────────────────────────────────
    | PristineCheck ->
        Make.target [] "Verify no pending changes" <| fun ctx ->
            if ctx.IsSet(cleanCheckout) then
                printfn "Checkout is dirty but --clean-checkout was specified, skipping check"
            else
                let r = exec { binary "git"; arguments ["status"; "--porcelain"]; output }
                if r.ConsoleOut |> Seq.isEmpty |> not then
                    failBuild "The checkout folder has pending changes, aborting"
                printfn "The checkout folder does not have pending changes, proceeding"

    // ── test ───────────────────────────────────────────────────────────────
    | Test opts ->
        Make.target [Build] "Run all tests" <| fun _ ->
            exec { run "dotnet"
                       (["test"; "-c"; "RELEASE"; "--logger:GithubActions"; "--logger:pretty"]
                        @ (opts.Filter |> Option.map (sprintf "--filter:%s") |> Option.toList)) }

    // ── release notes ──────────────────────────────────────────────────────
    | GenerateReleaseNotes ->
        Make.target [] "" <| fun ctx ->
            let ver        = currentVersion.Value
            let outputFile = Path.Combine(outputPath(), sprintf "release-notes-%s.md" ver)
            let tokenArgs  = ctx.Get(token) |> Option.map (fun t -> ["--token"; t]) |> Option.defaultValue []
            let repoArgs   = Repository.Split('/') |> Array.toList
            exec { run "dotnet"
                       (["release-notes"] @ repoArgs
                        @ ["--version"; ver
                           "--label"; "enhancement"; "New Features"
                           "--label"; "bug";         "Bug Fixes"
                           "--label"; "documentation";"Docs Improvements"
                           "--output"; outputFile]
                        @ tokenArgs) }

    // ── api changes ────────────────────────────────────────────────────────
    | GenerateApiChanges ->
        Make.target [] "" <| fun _ ->
            let ver = currentVersion.Value
            let assembliesDir id =
                match id with
                | "Nullean.Argh.Hosting" | "Nullean.Argh.Interfaces" ->
                    $".artifacts/bin/%s{id}/release_%s{MainTfm}"
                | _ -> $".artifacts/bin/%s{id}/release"

            output().GetFiles("*.nupkg")
            |> Seq.sortByDescending _.CreationTimeUtc
            |> Seq.map  (fun f -> packageIdFromFile (Path.GetRelativePath(Directory.GetCurrentDirectory(), f.FullName)))
            |> Seq.filter (fun p -> p <> "Nullean.Argh")
            |> Seq.iter (fun pkg ->
                exec { run "dotnet"
                           ["assembly-differ"
                            $"previous-nuget|%s{pkg}|%s{ver}|%s{MainTfm}"
                            $"directory|%s{assembliesDir pkg}"
                            "-a"; "true"; "--target"; pkg; "-f"; "github-comment"
                            "--output"; Path.Combine(outputPath(), $"breaking-changes-%s{pkg}.md")] })

    // ── create GitHub release ──────────────────────────────────────────────
    | CreateReleaseOnGithub ->
        Make.target [] "" <| fun ctx ->
            let ver          = currentVersion.Value
            let releaseNotes = Path.Combine(outputPath(), $"release-notes-%s{ver}.md")
            let tokenArgs    = ctx.Get(token) |> Option.map (fun t -> ["--token"; t]) |> Option.defaultValue []
            let bodyArgs     =
                output().GetFiles("breaking-changes-*.md")
                |> Seq.collect (fun f -> ["--body"; Path.GetRelativePath(Directory.GetCurrentDirectory(), f.FullName)])
                |> Seq.toList
            exec { run "dotnet"
                       (["release-notes"] @ (Repository.Split('/') |> Array.toList)
                        @ ["create-release"; "--version"; ver; "--body"; releaseNotes]
                        @ bodyArgs @ tokenArgs) }

    // ── commands ───────────────────────────────────────────────────────────
    | Release ->
        Make.command
            [ PristineCheck; Test defaultTest ]
            [ Pkg Generate; Pkg Validate; GenerateReleaseNotes; GenerateApiChanges ]

    | Publish ->
        Make.command
            [ Release ]
            [ CreateReleaseOnGithub ]

let argv = fsi.CommandLineArgs |> Array.skip 1
exit (app.RunAsync(argv).GetAwaiter().GetResult())
