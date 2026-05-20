#!/usr/bin/env -S dotnet fsi
// Local references — all transitive deps are co-located in the Nullean.Make.Fs output folder.
// Build first with: dotnet build -c Release
// Once Nullean.Make.Fs ships on NuGet, replace with:
//   #r "nuget: Nullean.Make.Fs, <version>"
#I ".artifacts/bin/Nullean.Make.Fs/release"
#r "Nullean.Make.Fs.dll"
#r "nuget: Proc, 0.13.0"

// F# build pipeline for nullean/argh using Nullean.Make.Fs.
//
// For local dev, run via the existing shebang wrapper:
//   ./build.sh <target>                  (current working entry point)
//   dotnet run --project build/scripts   (same thing)
//
// The DU below is intentionally exhaustive: adding a new case is a compile
// error until you handle it in app.Bind. Namespaces (Schema / Pkg) are marker
// cases that do not carry payloads.

open System
open System.IO
open Nullean.Make.Fs   // MakeApp<'T>, Make module, FsContext, MakeException
open ProcNet

// ── constants ─────────────────────────────────────────────────────────────────

let [<Literal>] Repository    = "nullean/argh"
let [<Literal>] MainTfm       = "netstandard2.0"
let [<Literal>] SignKey        = "96c599bbe3e70f5d"
let [<Literal>] IncludeGitHash = true

let output () = DirectoryInfo(Path.Combine("build", "output"))

let outputPath () =
    Path.GetRelativePath(Directory.GetCurrentDirectory(), (output()).FullName)

let schemaToolBin () =
    let name = "Nullean.Argh.SchemaExport"
    if Environment.OSVersion.Platform = PlatformID.Win32NT then
        sprintf ".artifacts/bin/%s/release/%s.exe" name name
    else
        sprintf ".artifacts/bin/%s/release/%s" name name

let exec binary (args: string list) =
    Proc.Exec(binary, args |> List.toArray) |> ignore

// ── version helpers (lazy, computed once) ─────────────────────────────────────

let restoreTools =
    lazy (exec "dotnet" [ "tool"; "restore" ])

let currentVersion =
    lazy (
        restoreTools.Value |> ignore
        let r = Proc.Start("dotnet", "minver", "-p", "canary.0", "-m", "0.1")
        let o = r.ConsoleOut |> Seq.find (fun l -> not (l.Line.StartsWith("MinVer:")))
        o.Line
    )

let currentVersionInformational =
    lazy (
        if IncludeGitHash then
            let hash = Proc.Start("git", "rev-parse", "--short", "HEAD").ConsoleOut |> Seq.head
            sprintf "%s+%s" currentVersion.Value (hash.Line.Trim())
        else
            currentVersion.Value
    )

let packageIdFromFile (path: string) =
    Path.GetFileNameWithoutExtension(path).Replace("." + currentVersion.Value, "")

// ── target / command DU ───────────────────────────────────────────────────────

type Target =
    // namespace markers
    | Schema
    | Pkg
    // atomic targets
    | Clean
    | Build
    | PristineCheck
    | Test of TestOptions
    | GenerateReleaseNotes
    | GenerateApiChanges
    | CreateReleaseOnGithub
    // schema sub-targets
    | SchemaUpdate
    | SchemaValidate
    // pkg sub-targets
    | PkgGenerate
    | PkgValidate
    // commands
    | Release
    | Publish

and TestOptions = { Filter: string option }

let defaultTest = { Filter = None }

// ── global options ────────────────────────────────────────────────────────────

let app = MakeApp<Target>("argh-build", Some "Build pipeline for nullean/argh")

let cleanCheckout = app.Flag("--clean-checkout", short = "-c", desc = "Skip the clean-checkout guard")
let token         = app.Option<string option>("--token", desc = "GitHub token for release/publish", defaultValue = None)

// ── single exhaustive binding ─────────────────────────────────────────────────

app.Bind(fun case ->
    match case with

    // namespaces
    | Schema -> Make.ns "schema" (Some "Schema export operations")
    | Pkg    -> Make.ns "pkg"    (Some "NuGet package operations")

    // ── clean ──────────────────────────────────────────────────────────────
    | Clean ->
        Make.target "Delete build output" [] (fun _ ->
            let out = output ()
            if out.Exists then out.Delete(true)
            exec "dotnet" [ "clean" ])

    // ── build ──────────────────────────────────────────────────────────────
    | Build ->
        Make.target "dotnet build -c Release" [ Clean ] (fun _ ->
            exec "dotnet" [ "build"; "-c"; "Release" ])

    // ── pristine-check ─────────────────────────────────────────────────────
    | PristineCheck ->
        Make.target "Verify no pending changes" [] (fun ctx ->
            if ctx.IsSet(cleanCheckout) then
                printfn "Checkout is dirty but --clean-checkout was specified, skipping check"
            else
                let r = Proc.Start("git", "status", "--porcelain")
                if r.ConsoleOut |> Seq.isEmpty |> not then
                    raise (MakeException("The checkout folder has pending changes, aborting"))
                printfn "The checkout folder does not have pending changes, proceeding")

    // ── test ───────────────────────────────────────────────────────────────
    | Test opts ->
        Make.target "Run all tests" [ Build ] (fun _ ->
            let args =
                [ "test"; "-c"; "RELEASE"; "--logger:GithubActions"; "--logger:pretty" ]
                @ (opts.Filter |> Option.map (sprintf "--filter:%s") |> Option.toList)
            exec "dotnet" args)

    // ── release notes ──────────────────────────────────────────────────────
    | GenerateReleaseNotes ->
        Make.target "Generate release-notes-<ver>.md" [] (fun ctx ->
            let ver        = currentVersion.Value
            let outputFile = Path.Combine(outputPath(), sprintf "release-notes-%s.md" ver)
            let tokenArgs  = ctx.Get(token) |> Option.map (fun t -> [ "--token"; t ]) |> Option.defaultValue []
            let repoArgs   = Repository.Split('/') |> Array.toList
            exec "dotnet"
                ( [ "release-notes" ] @ repoArgs
                @ [ "--version"; ver
                    "--label"; "enhancement"; "New Features"
                    "--label"; "bug";         "Bug Fixes"
                    "--label"; "documentation";"Docs Improvements"
                    "--output"; outputFile ]
                @ tokenArgs ))

    // ── api changes ────────────────────────────────────────────────────────
    | GenerateApiChanges ->
        Make.target "Generate breaking-changes-<pkg>.md files" [] (fun _ ->
            let ver = currentVersion.Value
            let assembliesDir id =
                match id with
                | "Nullean.Argh.Hosting" | "Nullean.Argh.Interfaces" ->
                    sprintf ".artifacts/bin/%s/release_%s" id MainTfm
                | _ -> sprintf ".artifacts/bin/%s/release" id
            let out = output ()
            out.GetFiles("*.nupkg")
            |> Seq.sortByDescending (fun f -> f.CreationTimeUtc)
            |> Seq.map  (fun f -> packageIdFromFile (Path.GetRelativePath(Directory.GetCurrentDirectory(), f.FullName)))
            |> Seq.filter (fun p -> p <> "Nullean.Argh")
            |> Seq.iter (fun pkg ->
                exec "dotnet"
                    [ "assembly-differ"
                      sprintf "previous-nuget|%s|%s|%s" pkg ver MainTfm
                      sprintf "directory|%s" (assembliesDir pkg)
                      "-a"; "true"; "--target"; pkg; "-f"; "github-comment"
                      "--output"; Path.Combine(outputPath(), sprintf "breaking-changes-%s.md" pkg) ]))

    // ── create github release ──────────────────────────────────────────────
    | CreateReleaseOnGithub ->
        Make.target "Create GitHub release with notes and API-diff bodies" [] (fun ctx ->
            let ver         = currentVersion.Value
            let releaseNotes = Path.Combine(outputPath(), sprintf "release-notes-%s.md" ver)
            let tokenArgs   = ctx.Get(token) |> Option.map (fun t -> [ "--token"; t ]) |> Option.defaultValue []
            let bodyArgs    =
                output().GetFiles("breaking-changes-*.md")
                |> Seq.collect (fun f -> [ "--body"; Path.GetRelativePath(Directory.GetCurrentDirectory(), f.FullName) ])
                |> Seq.toList
            exec "dotnet"
                ( [ "release-notes" ] @ (Repository.Split('/') |> Array.toList)
                @ [ "create-release"; "--version"; ver; "--body"; releaseNotes ]
                @ bodyArgs @ tokenArgs ))

    // ── schema sub-targets ─────────────────────────────────────────────────
    | SchemaUpdate ->
        Make.target "Regenerate schema/argh-cli-schema.json" [] (fun _ ->
            exec "dotnet" [ "build"; "-c"; "Release"; "tools/Nullean.Argh.SchemaExport" ]
            if not (Directory.Exists "schema") then Directory.CreateDirectory "schema" |> ignore
            exec (schemaToolBin()) [ "--out"; "schema/argh-cli-schema.json" ])

    | SchemaValidate ->
        Make.target "Fail if schema/argh-cli-schema.json is out of date" [] (fun _ ->
            exec "dotnet" [ "build"; "-c"; "Release"; "tools/Nullean.Argh.SchemaExport" ]
            let tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json")
            try
                exec (schemaToolBin()) [ "--out"; tempPath ]
                let generated = File.ReadAllText(tempPath).TrimEnd()
                let existing  = File.ReadAllText("schema/argh-cli-schema.json").TrimEnd()
                if generated <> existing then
                    raise (MakeException("schema/argh-cli-schema.json is out of date. Run: ./build.sh schema update"))
            finally
                if File.Exists tempPath then File.Delete tempPath)

    // ── pkg sub-targets ────────────────────────────────────────────────────
    | PkgGenerate ->
        Make.target "dotnet pack -c Release" [] (fun _ ->
            let out = output ()
            if out.Exists then out.Delete(true)
            exec "dotnet" [ "pack"; "-c"; "Release"; "-o"; outputPath() ])

    | PkgValidate ->
        Make.target "Run nupkg-validator on each .nupkg" [] (fun _ ->
            let baseArgs = [ "-v"; currentVersionInformational.Value; "-k"; SignKey; "-t"; outputPath() ]
            output().GetFiles("*.nupkg")
            |> Seq.sortByDescending (fun f -> f.CreationTimeUtc)
            |> Seq.map  (fun f -> Path.GetRelativePath(Directory.GetCurrentDirectory(), f.FullName))
            |> Seq.filter (fun p -> packageIdFromFile p <> "Nullean.Argh")
            |> Seq.iter (fun p -> exec "dotnet" ([ "nupkg-validator"; p ] @ baseArgs)))

    // ── commands ───────────────────────────────────────────────────────────
    | Release ->
        Make.command
            "Verify gates → pack → release-notes → api-diff"
            [ PristineCheck; Test defaultTest ]
            [ PkgGenerate; PkgValidate; GenerateReleaseNotes; GenerateApiChanges ]

    | Publish ->
        Make.command
            "Release → create GitHub release"
            [ Release ]
            [ CreateReleaseOnGithub ]
)

let argv = fsi.CommandLineArgs |> Array.skip 1
exit (app.RunAsync(argv).GetAwaiter().GetResult())
