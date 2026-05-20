#!/usr/bin/env -S dotnet run --
#:package Proc@0.13.0
#:project src/Nullean.Make
#:property ManagePackageVersionsCentrally=false
// C# build pipeline for nullean/argh using Nullean.Make.
//
// Once Nullean.Make is on NuGet, activate with:
//   #:package Nullean.Make@<version>
//   #:package Proc@0.13.0
//

using Nullean.Make;
using ProcNet;

// ReSharper disable ArrangeTypeMemberModifiers
// ReSharper disable ArrangeTypeModifiers

// ── constants ─────────────────────────────────────────────────────────────────

await MakeApp.Execute<Make>(args);

// ── per-target DTOs ───────────────────────────────────────────────────────────
record TestOptions(string? Filter = null);

// ── build class ───────────────────────────────────────────────────────────────

class Make : MakeBuild
{
	const string Repository    = "nullean/argh";
	const string MainTfm       = "netstandard2.0";
	const string SignKey        = "96c599bbe3e70f5d";
	const bool   IncludeGitHash = true;

	static DirectoryInfo Output = new(Path.Combine("build", "output"));

	static string OutputPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), Output.FullName);

	static string SchemaToolBin() =>
		OperatingSystem.IsWindows()
			? ".artifacts/bin/Nullean.Argh.SchemaExport/release/Nullean.Argh.SchemaExport.exe"
			: ".artifacts/bin/Nullean.Argh.SchemaExport/release/Nullean.Argh.SchemaExport";

	// ── version helpers (computed once, lazily) ───────────────────────────────────

	static Lazy<string> _version = new(static () =>
	{
		Proc.Exec("dotnet", new[] { "tool", "restore" });
		return Proc.Start("dotnet", "minver", "-p", "canary.0", "-m", "0.1")
			.ConsoleOut.First(l => !l.Line.StartsWith("MinVer:")).Line;
	});

	static string CurrentVersion = _version.Value;

	static string CurrentVersionInformational = IncludeGitHash
		? $"{CurrentVersion}+{Proc.Start("git", "rev-parse", "--short", "HEAD").ConsoleOut.First().Line.Trim()}"
		: CurrentVersion;

	static string PackageIdFromFile(string path) => Path.GetFileNameWithoutExtension(path).Replace("." + CurrentVersion, "");




    [Flag("--clean-checkout", "-c", Description = "Skip the clean-checkout guard on release/publish")]
    public bool CleanCheckout { get; init; }

    [GlobalOption("--token", Description = "GitHub token for generating release notes and creating releases")]
    public string? Token { get; init; }

    // ── atomic targets ────────────────────────────────────────────────────────

    public Target Clean => _ => _
        .Description("Delete build output")
        .Executes(() =>
        {
            if (Output.Exists) Output.Delete(true);
            Proc.Exec("dotnet", new[] { "clean" });
        });

    public Target Build => _ => _
        .Description("dotnet build -c Release")
        .DependsOn(Clean)
        .Executes(() => Proc.Exec("dotnet", new[] { "build", "-c", "Release" }));

    public Target PristineCheck => _ => _
        .Hidden()
        .OnlyWhen(() => !CleanCheckout)
        .Executes(() =>
        {
            var dirty = Proc.Start("git", "status", "--porcelain").ConsoleOut.Any();
            if (dirty)
                throw new MakeException("The checkout folder has pending changes, aborting");
            Console.WriteLine("The checkout folder does not have pending changes, proceeding");
        });

    public Target<TestOptions> Test => _ => _
        .Description("Run all tests")
        .DependsOn(Build)
        .Executes(opts =>
        {
            var argList = new List<string> { "test", "-c", "RELEASE", "--logger:GithubActions", "--logger:pretty" };
            if (opts.Filter is not null) argList.Add($"--filter:{opts.Filter}");
            Proc.Exec("dotnet", argList.ToArray());
        });

    public Target GenerateReleaseNotes => _ => _
        .Hidden()
        .Executes(() =>
        {
            var ver        = CurrentVersion;
            var outputFile = Path.Combine(OutputPath, $"release-notes-{ver}.md");
            var tokenArgs  = Token is not null ? new[] { "--token", Token } : Array.Empty<string>();
            Proc.Exec("dotnet", new[] { "release-notes" }
                .Concat(Repository.Split('/'))
                .Concat(new[]
                {
                    "--version", ver,
                    "--label", "enhancement",  "New Features",
                    "--label", "bug",          "Bug Fixes",
                    "--label", "documentation","Docs Improvements",
                    "--output", outputFile
                })
                .Concat(tokenArgs).ToArray());
        });

    public Target GenerateApiChanges => _ => _
        .Hidden()
        .Executes(() =>
        {
            var ver = CurrentVersion;
            static string AssembliesDir(string id) => id is "Nullean.Argh.Hosting" or "Nullean.Argh.Interfaces"
                ? $".artifacts/bin/{id}/release_{MainTfm}"
                : $".artifacts/bin/{id}/release";

            foreach (var pkg in Output.GetFiles("*.nupkg")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => PackageIdFromFile(Path.GetRelativePath(Directory.GetCurrentDirectory(), f.FullName)))
                .Where(p => p != "Nullean.Argh"))
            {
                Proc.Exec("dotnet", new[]
                {
                    "assembly-differ",
                    $"previous-nuget|{pkg}|{ver}|{MainTfm}",
                    $"directory|{AssembliesDir(pkg)}",
                    "-a", "true", "--target", pkg, "-f", "github-comment",
                    "--output", Path.Combine(OutputPath, $"breaking-changes-{pkg}.md")
                });
            }
        });

    public Target CreateReleaseOnGithub => _ => _
        .Hidden()
        .Executes(() =>
        {
            var ver         = CurrentVersion;
            var releaseNotes = Path.Combine(OutputPath, $"release-notes-{ver}.md");
            var tokenArgs    = Token is not null ? new[] { "--token", Token } : Array.Empty<string>();
            var bodyArgs     = Output.GetFiles("breaking-changes-*.md")
                .SelectMany(f => new[] { "--body", Path.GetRelativePath(Directory.GetCurrentDirectory(), f.FullName) });

            Proc.Exec("dotnet", new[] { "release-notes" }
                .Concat(Repository.Split('/'))
                .Concat(new[] { "create-release", "--version", ver, "--body", releaseNotes })
                .Concat(bodyArgs).Concat(tokenArgs).ToArray());
        });

    // ── schema namespace ──────────────────────────────────────────────────────

    public class Schema : Namespace
    {
        public Target Update => _ => _
            .Description("Regenerate schema/argh-cli-schema.json")
            .Executes(() =>
            {
                Proc.Exec("dotnet", new[] { "build", "-c", "Release", "tools/Nullean.Argh.SchemaExport" });
                if (!Directory.Exists("schema")) Directory.CreateDirectory("schema");
                Proc.Exec(SchemaToolBin(), new[] { "--out", "schema/argh-cli-schema.json" });
            });

        public Target Validate => _ => _
            .Description("Fail if schema/argh-cli-schema.json is out of date")
            .Executes(() =>
            {
                Proc.Exec("dotnet", new[] { "build", "-c", "Release", "tools/Nullean.Argh.SchemaExport" });
                var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
                try
                {
                    Proc.Exec(SchemaToolBin(), new[] { "--out", tempPath });
                    if (File.ReadAllText(tempPath).TrimEnd() != File.ReadAllText("schema/argh-cli-schema.json").TrimEnd())
                        throw new MakeException("schema/argh-cli-schema.json is out of date. Run: ./build.sh schema update");
                }
                finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
            });
    }

    // ── pkg namespace ─────────────────────────────────────────────────────────

    public class Pkg : Namespace
    {
        public Target Generate => _ => _
            .Hidden()
            .Executes(() =>
            {
                if (Output.Exists) Output.Delete(true);
                Proc.Exec("dotnet", new[] { "pack", "-c", "Release", "-o", OutputPath });
            });

        public Target Validate => _ => _
            .Hidden()
            .Executes(() =>
            {
                var baseArgs = new[] { "-v", CurrentVersionInformational, "-k", SignKey, "-t", OutputPath };
                foreach (var pkg in Output.GetFiles("*.nupkg")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Select(f => Path.GetRelativePath(Directory.GetCurrentDirectory(), f.FullName))
                    .Where(p => PackageIdFromFile(p) != "Nullean.Argh"))
                    Proc.Exec("dotnet", new[] { "nupkg-validator", pkg }.Concat(baseArgs).ToArray());
            });
    }

    // ── commands ──────────────────────────────────────────────────────────────

    public Command Release { get; }
    public Command Publish { get; }

    public Make()
    {
        var pkg = new Pkg();

        Release = _ => _
            .Description("Verify gates → pack → release-notes → api-diff")
            .Requires(PristineCheck, Test)
            .Composes(pkg.Generate, pkg.Validate, GenerateReleaseNotes, GenerateApiChanges);

        Publish = _ => _
            .Description("Release → create GitHub release")
            .Requires(Release)
            .Composes(CreateReleaseOnGithub);
    }
}

