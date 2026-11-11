module Program

open System
open System.IO
open Nullean.Make.Fs
open Proc.Fs

/// The build's targets; `dotnet run --project build/scripts -- --help` lists them with these descriptions
type Target =
    /// clean known output locations
    | Clean
    /// Run build
    | Build
    /// Runs build then tests
    | Test
    /// Runs build, then creates and validates the packages and reports public API changes
    | Pack
    /// Runs tests, checks the schema is up to date, then creates and validates the packages: exactly what CI runs
    | Ci
    /// Pack the NuGet packages into build/output
    | Generatepackages
    /// Validate the packed NuGet packages
    | Validatepackages
    /// Report public API changes against the previous release
    | Generateapichanges
    /// Run the schema export tool and write schema/argh-cli-schema.json
    | Updateschema
    /// Fail if schema/argh-cli-schema.json is out of date
    | Validateschema
    /// Build the documentation site and serve it locally
    | Docs

/// build/output, relative to the repository root
let private output = Paths.RootRelative Paths.Output.FullName

/// The packed .nupkg files, newest first, without the Nullean.Argh metapackage: it has no assemblies to validate or diff
let private packageIds () =
    let version = Versions.current.Value
    Paths.Output.GetFiles("*.nupkg")
    |> Seq.sortByDescending (fun f -> f.CreationTimeUtc)
    |> Seq.map (fun f -> Path.GetFileNameWithoutExtension(f.Name).Replace("." + version, ""), Paths.RootRelative f.FullName)
    |> Seq.filter (fun (id, _) -> id <> "Nullean.Argh")
    |> Seq.toList

/// Every project but this build itself: `dotnet clean` on the solution would delete the assemblies the running
/// build still loads lazily and fail the next target
let private projectsToClean () =
    ["src"; "tests"; "examples"; "tools"]
    |> List.collect (fun d -> Directory.EnumerateFiles(d, "*.?sproj", SearchOption.AllDirectories) |> Seq.toList)

/// Unified artifacts layout (<UseArtifactsOutput>): `.artifacts/bin/<Project>/release[_<tfm>]/`
let private assembliesDir (packageId: string) =
    match packageId with
    | "Nullean.Argh.Hosting" | "Nullean.Argh.Interfaces" | "Nullean.Argh.Core" -> $".artifacts/bin/{packageId}/release_{Paths.MainTFM}"
    | _ -> $".artifacts/bin/{packageId}/release"

let private schemaTool =
    let name = "Nullean.Argh.SchemaExport"
    if OperatingSystem.IsWindows() then $".artifacts/bin/{name}/release/{name}.exe" else $".artifacts/bin/{name}/release/{name}"

[<EntryPoint>]
let main argv =
    let app = MakeApp<Target>("dotnet run --project build/scripts --", None)

    // `-s` skips what a target `requires`, never what it `composes`: build always cleans first
    app.Bind <| function
        | Clean -> target [] {
              if Paths.Output.Exists then Paths.Output.Delete true
              for project in projectsToClean () do
                  sh $"dotnet clean {project} -c Release -v q" }
        | Build -> command {
              composes [Clean]
              executes (shell { sh $"dotnet build -c Release" }) }
        // global.json selects Microsoft.Testing.Platform, so this runs the TUnit test executables directly; on GitHub
        // Actions TUnit writes its own summary of the run to GITHUB_STEP_SUMMARY
        | Test -> target [Build] { sh $"dotnet test -c Release" }
        | Pack -> command { requires [Build]; composes [Generatepackages; Validatepackages; Generateapichanges] }
        | Ci -> command { requires [Test]; composes [Validateschema; Generatepackages; Validatepackages; Generateapichanges] }
        | Generatepackages -> target [] {
              if Paths.Output.Exists then Paths.Output.Delete true
              sh $"dotnet pack -c Release -o {output}" }
        | Validatepackages -> target [] {
              let version = Versions.informational.Value
              let jenkinsOnWindows =
                  not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable "JENKINS_URL")) && OperatingSystem.IsWindows()
              for _, package in packageIds () do
                  if jenkinsOnWindows then
                      sh $"dotnet nupkg-validator validate {package} -v {version} -k {Paths.SignKey} -t {output} -r"
                  else
                      sh $"dotnet nupkg-validator validate {package} -v {version} -k {Paths.SignKey} -t {output}" }
        | Generateapichanges -> target [] {
              let version = Versions.current.Value
              for id, _ in packageIds () do
                  let report = Path.Combine(output, $"breaking-changes-{id}.md")
                  // quoted: an unquoted | would pipe
                  sh $"dotnet assembly-differ 'previous-nuget|{id}|{version}|{Paths.MainTFM}' 'directory|{assembliesDir id}' -a true --target {id} -f github-comment --output {report}" }
        | Updateschema -> target [] {
              sh $"dotnet build -c Release tools/Nullean.Argh.SchemaExport"
              Directory.CreateDirectory "schema" |> ignore
              sh $"{schemaTool} --out schema/argh-cli-schema.json" }
        | Validateschema -> target [] {
              sh $"dotnet build -c Release tools/Nullean.Argh.SchemaExport"
              let generated = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json")
              try
                  sh $"{schemaTool} --out {generated}"
                  if File.ReadAllText(generated).TrimEnd() <> File.ReadAllText("schema/argh-cli-schema.json").TrimEnd() then
                      failBuild "schema/argh-cli-schema.json is out of date. Run: dotnet run --project build/scripts -- updateschema"
              finally
                  if File.Exists generated then File.Delete generated }
        | Docs -> target [] {
              Documentation.build ()
              Documentation.serve 8080 }

    app.RunAsync(argv).GetAwaiter().GetResult()
