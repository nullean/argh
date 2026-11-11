using Nullean.Make.Discovery;

namespace Nullean.Make.Wrappers;

/// <summary>
/// Reserved commands every Make entry point answers before parsing targets. The <c>__</c> prefix follows Argh's
/// meta-commands (<c>__schema</c>, <c>__complete</c>), so they never collide with a target name.
/// </summary>
internal static class MakeIntrinsics
{
	/// <summary>Runs a reserved command when <paramref name="args"/> starts with one.</summary>
	/// <returns>The exit code, or <c>null</c> when <paramref name="args"/> is an ordinary target invocation.</returns>
	public static int? TryRun(string[] args, BuildGraph graph)
	{
		if (args.Length == 0 || args[0] != WrapperFiles.Command) return null;
		return WrapperFiles.Run(args.AsSpan(1).ToArray(), graph, Console.Out, Console.Error);
	}
}

/// <summary><c>__wrappers sync|check</c>: writes or verifies the mise, make and just include files.</summary>
internal static class WrapperFiles
{
	public const string Command = "__wrappers";
	private const string DefaultOutDir = ".make";

	private const string Usage = """
		Usage: __wrappers <sync|check> [options]

		  sync     Write the wrapper include files that changed
		  check    Exit 1 when a wrapper include file is missing or out of date

		Options:
		  --run <command>     Command the wrappers call, e.g. "dotnet run build.cs --".
		                      Default: the run: line of an existing file, else `dotnet run <script> --` for a file-based app
		  --out-dir <dir>     Where the files go (default: .make)
		  --only <list>       Comma-separated: mise, make, just. Default: the files already in --out-dir, else all three
		  --prefix <name>     Group every task under a name: mise <name>:test, make <name>/test, just <name>-test
		  --quiet             Print nothing unless a file changed or a check fails
		""";

	private sealed record Options(string Verb, string? Run, string OutDir, string[]? Only, string? Prefix, bool Quiet);

	public static int Run(string[] args, BuildGraph graph, TextWriter stdout, TextWriter stderr)
	{
		if (args.Length == 0 || args[0] is "-h" or "--help")
		{
			(args.Length == 0 ? stderr : stdout).WriteLine(Usage);
			return args.Length == 0 ? 2 : 0;
		}

		Options options;
		try { options = Parse(args); }
		catch (MakeException ex)
		{
			stderr.WriteLine(ex.Message);
			stderr.WriteLine(Usage);
			return ex.ExitCode;
		}

		var outDir = options.OutDir;
		var emitters = SelectEmitters(options, outDir, out var unknown);
		if (unknown is not null)
		{
			stderr.WriteLine($"Unknown wrapper '{unknown}' in --only. Known: {string.Join(", ", WrapperEmitters.All.Select(e => e.Key))}.");
			return 2;
		}
		if (emitters.Count == 0)
		{
			stderr.WriteLine($"--only names no wrapper. Known: {string.Join(", ", WrapperEmitters.All.Select(e => e.Key))}.");
			return 2;
		}

		var recorded = ReadRecorded(outDir);
		var run = options.Run ?? recorded.Run ?? DetectRun();
		if (run is null)
		{
			stderr.WriteLine("Cannot tell how to run this build: pass --run, e.g. --run \"dotnet run --project build --\".");
			return 2;
		}
		var prefix = options.Prefix ?? recorded.Prefix;

		var model = WrapperModel.From(graph, run, string.IsNullOrEmpty(prefix) ? null : prefix);
		foreach (var e in emitters)
		{
			var clash = model.Targets
				.GroupBy(t => model.NameOf(t, e.Separator), StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault(g => g.Count() > 1);
			if (clash is null) continue;
			var routes = string.Join(" and ", clash.Select(t => $"'{string.Join(" ", t.Route)}'"));
			stderr.WriteLine($"{routes} would both be the {e.Key} task '{clash.Key}'. Rename one of them.");
			return 2;
		}

		return options.Verb == "check"
			? Check(model, emitters, outDir, stdout, stderr, options.Quiet)
			: Sync(model, emitters, outDir, stdout, options);
	}

	private static int Sync(WrapperModel model, IReadOnlyList<IWrapperEmitter> emitters, string outDir, TextWriter stdout, Options options)
	{
		Directory.CreateDirectory(outDir);
		var created = new List<IWrapperEmitter>();
		foreach (var e in emitters)
		{
			var path = Path.Combine(outDir, e.FileName);
			var content = e.Emit(model);
			var exists = File.Exists(path);
			if (exists && File.ReadAllText(path) == content)
			{
				if (!options.Quiet) stdout.WriteLine($"unchanged {Display(path)}");
				continue;
			}
			File.WriteAllText(path, content);
			stdout.WriteLine($"wrote {Display(path)} ({model.Targets.Count} targets)");
			if (!exists) created.Add(e);
		}

		if (created.Count > 0 && !options.Quiet)
			PrintSetup(model, created, outDir, stdout, options);
		return 0;
	}

	private static int Check(WrapperModel model, IReadOnlyList<IWrapperEmitter> emitters, string outDir, TextWriter stdout, TextWriter stderr, bool quiet)
	{
		var stale = new List<string>();
		foreach (var e in emitters)
		{
			var path = Path.Combine(outDir, e.FileName);
			if (!File.Exists(path)) stale.Add($"{Display(path)} is missing");
			else if (File.ReadAllText(path) != e.Emit(model)) stale.Add($"{Display(path)} is out of date");
		}

		if (stale.Count == 0)
		{
			if (!quiet) stdout.WriteLine($"{emitters.Count} wrapper file(s) up to date");
			return 0;
		}
		foreach (var s in stale) stderr.WriteLine(s);
		stderr.WriteLine($"Run: {model.Run} {Command} sync");
		return 1;
	}

	private static void PrintSetup(WrapperModel model, IReadOnlyList<IWrapperEmitter> created, string outDir, TextWriter stdout, Options options)
	{
		stdout.WriteLine();
		foreach (var e in created)
		{
			var rel = Display(Path.Combine(outDir, e.FileName));
			var target = e.Key switch { "mise" => "mise.toml", "make" => "Makefile", _ => "justfile" };
			stdout.WriteLine($"Add to {target}:");
			foreach (var line in e.IncludeHint(rel).Split('\n')) stdout.WriteLine($"  {line}");
			stdout.WriteLine();
		}

		// mise hooks keep every wrapper file fresh, so the output directory can be gitignored.
		var syncArgs = new List<string> { Command, "sync", "--quiet" };
		if (options.Run is not null && options.Run != DetectRun()) syncArgs.AddRange(["--run", ShellQuote(options.Run)]);
		if (options.OutDir != DefaultOutDir) syncArgs.AddRange(["--out-dir", ShellQuote(options.OutDir)]);
		if (options.Only is not null) syncArgs.AddRange(["--only", string.Join(",", options.Only)]);
		if (model.Prefix is not null) syncArgs.AddRange(["--prefix", ShellQuote(model.Prefix)]);
		var hook = $"{model.Run} {string.Join(" ", syncArgs)}";

		stdout.WriteLine("To regenerate them from mise (then the directory can be gitignored), add to mise.toml:");
		stdout.WriteLine("  [hooks]");
		stdout.WriteLine($"  postinstall = {Toml.Basic(hook)}");
		var script = EntryPointFile();
		if (script is not null)
		{
			stdout.WriteLine("  [[watch_files]]");
			stdout.WriteLine($"  patterns = [{Toml.Basic(script)}]");
			stdout.WriteLine($"  run = {Toml.Basic(hook)}");
		}
		stdout.WriteLine();
	}

	private static Options Parse(string[] args)
	{
		var verb = args[0];
		if (verb is not ("sync" or "check"))
			throw new MakeException($"Unknown {Command} command '{verb}'.", 2);

		string? run = null, prefix = null;
		string outDir = DefaultOutDir;
		string[]? only = null;
		var quiet = false;
		for (var i = 1; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--run": run = Value(args, ref i); break;
				case "--out-dir": outDir = Value(args, ref i); break;
				case "--prefix": prefix = Value(args, ref i); break;
				case "--only":
					only = Value(args, ref i).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
					break;
				case "--quiet": quiet = true; break;
				default: throw new MakeException($"Unknown option '{args[i]}' for {Command} {verb}.", 2);
			}
		}
		return new Options(verb, run, outDir, only, prefix, quiet);
	}

	private static string Value(string[] args, ref int i)
	{
		if (i + 1 >= args.Length) throw new MakeException($"Missing value for '{args[i]}'.", 2);
		return args[++i];
	}

	private static List<IWrapperEmitter> SelectEmitters(Options options, string outDir, out string? unknown)
	{
		unknown = null;
		if (options.Only is { } only)
		{
			var selected = new List<IWrapperEmitter>();
			foreach (var key in only)
			{
				var e = WrapperEmitters.All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
				if (e is null) { unknown = key; return []; }
				if (!selected.Contains(e)) selected.Add(e);
			}
			return selected;
		}
		var existing = WrapperEmitters.All.Where(e => File.Exists(Path.Combine(outDir, e.FileName))).ToList();
		return existing.Count > 0 ? existing : WrapperEmitters.All.ToList();
	}

	/// <summary>The <c>run:</c> and <c>prefix:</c> header lines of the first generated file in <paramref name="outDir"/>,
	/// whichever wrappers are selected.</summary>
	private static (string? Run, string? Prefix) ReadRecorded(string outDir)
	{
		foreach (var e in WrapperEmitters.All)
		{
			var path = Path.Combine(outDir, e.FileName);
			if (!File.Exists(path)) continue;
			string? run = null, prefix = null;
			foreach (var line in File.ReadLines(path))
			{
				if (!line.StartsWith('#')) break;
				if (line.StartsWith("# run: ", StringComparison.Ordinal)) run = line["# run: ".Length..];
				else if (line.StartsWith("# prefix: ", StringComparison.Ordinal)) prefix = line["# prefix: ".Length..];
			}
			if (run is not null) return (run, prefix);
		}
		return (null, null);
	}

	/// <summary><c>dotnet run &lt;script&gt; --</c> for a file-based app (<c>dotnet run build.cs</c>), else null.</summary>
	private static string? DetectRun() =>
		EntryPointFile() is { } script ? $"dotnet run {ShellQuote(script)} --" : null;

	/// <summary>The file-based app's entry file, relative to the working directory, with forward slashes.</summary>
	private static string? EntryPointFile() =>
		AppContext.GetData("EntryPointFilePath") is string { Length: > 0 } path ? Display(path) : null;

	private static string Display(string path) =>
		Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetFullPath(path)).Replace('\\', '/');

	private static string ShellQuote(string value) =>
		value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '/' or '-' or '_' or ':' or ',')
			? value
			: "'" + value.Replace("'", "'\\''") + "'";
}
