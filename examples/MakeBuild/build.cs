using Nullean.Argh;
using Nullean.Make;

return await MakeApp.Execute<Build>(args);

// ============================================================
class Build : MakeBuild
{
	[Flag("--clean-checkout", "-c", Description = "Bypass pristine-checkout check")]
	public bool CleanCheckout { get; set; }

	[GlobalOption("--token", Description = "GitHub token (release/publish)")]
	public string? Token { get; set; }

	// === Atomic targets ===
	public Target Clean => _ => _
		.Description("Delete build artifacts")
		.Executes(() => Console.WriteLine("dotnet clean"));

	public Target<CompileOptions> Compile => _ => _
		.Description("dotnet build with configurable verbosity")
		.Executes(opts => Console.WriteLine($"dotnet build -c {opts.Configuration} -v {opts.Verbosity}"));

	public Target<TestOptions> Test => _ => _
		.Description("Run all tests")
		.Executes(opts => Console.WriteLine($"dotnet test --logger:{opts.Logger}"));

	public Target PristineCheck => _ => _
		.Description("Verify checkout has no pending changes")
		.OnlyWhen(() => !CleanCheckout)
		.Executes(() => Console.WriteLine("git status --porcelain"));

	public Target ReleaseNotes => _ => _
		.Description("Generate RELEASE_NOTES.md")
		.Executes(() => Console.WriteLine($"release-notes --token {Token}"));

	public Target ApiChanges => _ => _.Executes(() => Console.WriteLine("api-changes"));
	public Target GhRelease => _ => _.Executes(() => Console.WriteLine("gh-release"));

	// === Commands — ctor used to capture Namespace targets by ref ===
	public Command Release { get; }
	public Command Publish { get; }

	public Build()
	{
		// Namespace instances created locally; MethodInfo identity means any instance works.
		var pkg = new Pkg();

		Release = _ => _
			.Description("Verify gates, pack, generate notes, diff APIs")
			.Requires(PristineCheck, Test)
			.Composes(pkg.Generate, pkg.Validate, ReleaseNotes, ApiChanges);

		Publish = _ => _
			.Description("Run release, push to GitHub")
			.Requires(Release)
			.Composes(GhRelease)
			.Executes(() => Console.WriteLine($"published at {DateTime.UtcNow:o}"));
	}

	// === Namespace classes — auto-discovered by the framework ===
	public class Schema : Namespace
	{
		public Target Update => _ => _
			.Description("Regenerate schema file")
			.Executes(() => Console.WriteLine("schema update"));

		public Target Validate => _ => _
			.Description("Diff against committed schema")
			.Executes(() => Console.WriteLine("schema validate"));
	}

	public class Pkg : Namespace
	{
		public Target<PackOptions> Generate => _ => _
			.Description("dotnet pack")
			.Executes(opts => Console.WriteLine($"dotnet pack -c {opts.Configuration} -o {opts.Output}"));

		public Target Validate => _ => _
			.Description("Run nupkg-validator")
			.Executes(() => Console.WriteLine("validate packages"));
	}
}

record CompileOptions(string Configuration = "Release", string Verbosity = "minimal");
record TestOptions(string Logger = "pretty", bool NoBuild = false, [Argument] string? Filter = null);
record PackOptions(string Configuration = "Release", string Output = ".artifacts");
