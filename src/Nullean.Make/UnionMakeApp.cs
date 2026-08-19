using Nullean.Make.Discovery;
using Nullean.Make.Execution;
using Nullean.Make.Help;
using Nullean.Make.Parsing;
using Nullean.Make.UnionGraph;

namespace Nullean.Make;

/// <summary>
/// Entry point for Make-based build scripts that use a C# 15 <c>union</c> type as the target identity.
/// Each case type of <typeparamref name="TUnion"/> maps to a CLI target; nested union case types become
/// namespace segments (e.g. <c>pkg/generate</c>).
/// <para>
/// Usage:
/// <code>
/// var app = new UnionMakeApp&lt;BuildTarget&gt;("my-build");
/// var token = app.Option&lt;string?&gt;("--token");
/// app.Bind(t =&gt; t switch {
///     Clean   =&gt; app.Target().Executes(() =&gt; ...),
///     Build b =&gt; app.Target().DependsOn&lt;Clean&gt;().Executes(() =&gt; ...),
///     Test  t =&gt; app.Target().DependsOn&lt;Build&gt;().Executes(() =&gt; Exec(t.Filter)),
/// });
/// return await app.RunAsync(args);
/// </code>
/// </para>
/// </summary>
/// <typeparam name="TUnion">A C# 15 union type whose case types are the build targets.</typeparam>
public sealed class UnionMakeApp<TUnion> where TUnion : struct
{
	private readonly BuildGraph _graph = new();
	private readonly List<UnionOptionDecl> _options = new();
	private Func<TUnion, IUnionTargetBuilder<TUnion>>? _bind;
	private Dictionary<string, string>? _typeNameToRoute;

	public UnionMakeApp(string name, string? description = null)
	{
		_graph.AppName        = name;
		_graph.AppDescription = description;
	}

	// ── Global option registration ────────────────────────────────────────────

	/// <summary>Registers a boolean flag. Read the returned ref's <c>Value</c> inside target bodies.</summary>
	public UnionOptionRef<bool> Flag(string longName, string? shortName = null, string? description = null)
	{
		var r = new UnionOptionRef<bool>(longName, shortName, description, false,
			s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1");
		_options.Add(new UnionOptionDecl(longName, shortName, description, IsFlag: true, Set: r.Set));
		_graph.GlobalOptions.Add(new GlobalOptionNode { Long = longName, Short = shortName, Description = description, IsFlag = true });
		return r;
	}

	/// <summary>Registers a typed option. Read the returned ref's <c>Value</c> inside target bodies.</summary>
	public UnionOptionRef<T> Option<T>(string longName, string? description = null, T defaultValue = default!)
	{
		var r = new UnionOptionRef<T>(longName, null, description, defaultValue,
			raw => (T)ParseRaw(typeof(T), raw, longName));
		_options.Add(new UnionOptionDecl(longName, null, description, IsFlag: false, Set: r.Set));
		_graph.GlobalOptions.Add(new GlobalOptionNode { Long = longName, Description = description, IsFlag = false });
		return r;
	}

	// ── Target / Command factories (used inside Bind) ─────────────────────────

	/// <summary>Creates a target builder for use inside <see cref="Bind"/>.</summary>
	public IUnionTargetBuilder<TUnion> Target()  => new UnionTargetBuilderImpl<TUnion>(TargetKind.Target);

	/// <summary>Creates a command builder for use inside <see cref="Bind"/>. Commands compose other targets.</summary>
	public IUnionTargetBuilder<TUnion> Command() => new UnionTargetBuilderImpl<TUnion>(TargetKind.Command);

	// ── Bind ──────────────────────────────────────────────────────────────────

	/// <summary>
	/// Provides the exhaustive mapping from union case to target definition.
	/// <para>
	/// The lambda is called twice per relevant case: once at graph-build time with default case
	/// values (for metadata: deps, description, kind) and once at execution time with real
	/// CLI-parsed case values (for the <c>Executes</c> closure). Side effects in switch arms
	/// before <c>Executes</c> will fire at graph-build time with default values — put side
	/// effects inside <c>Executes</c>.
	/// </para>
	/// </summary>
	public void Bind(Func<TUnion, IUnionTargetBuilder<TUnion>> fn) => _bind = fn;

	// ── RunAsync ──────────────────────────────────────────────────────────────

	/// <summary>
	/// Discovers union cases, validates the dependency graph, parses argv, and executes the
	/// requested target. Returns an exit code suitable for returning from top-level statements.
	/// </summary>
	public async Task<int> RunAsync(string[] args)
	{
		if (_bind is null) { Console.Error.WriteLine("[make] Bind() was not called."); return 1; }

		try
		{
			BuildGraph();
			ResolveRouteDeps();
			GraphValidator.Validate(_graph);
		}
		catch (MakeException ex) { Console.Error.WriteLine(ex.Message); return ex.ExitCode; }

		var scriptName = _graph.AppName;

		if (args.Length == 0) { MakeHelpPrinter.PrintRoot(_graph, scriptName); return 0; }

		var (remaining, singleTarget, showHelp, showVersion) = ExtractGlobals(args);

		if (showVersion) { Console.WriteLine("0.0.0"); return 0; }

		var (routeKey, targetArgs) = ResolveRoute(remaining);

		if (showHelp)
		{
			if (_graph.ByRoute.TryGetValue(routeKey, out var helpNode))
			{
				if (helpNode.Kind == TargetKind.Command) MakeHelpPrinter.PrintCommand(helpNode, _graph, scriptName);
				else                                      MakeHelpPrinter.PrintTarget(helpNode, _graph, scriptName);
			}
			else MakeHelpPrinter.PrintRoot(_graph, scriptName);
			return 0;
		}

		if (string.IsNullOrEmpty(routeKey))
		{
			var unknown = remaining.FirstOrDefault(t => !t.StartsWith("-"));
			if (unknown is not null) { Console.Error.WriteLine($"Unknown target '{unknown}'."); return 2; }
			MakeHelpPrinter.PrintRoot(_graph, scriptName);
			return 0;
		}

		if (!_graph.ByRoute.TryGetValue(routeKey, out var targetNode))
		{
			Console.Error.WriteLine($"Unknown target '{routeKey}'.");
			return 2;
		}

		// Wire up execution lambdas for every node (root gets real args, deps get empty args)
		WireExecutionBodies(targetNode, targetArgs);

		var parsed = new ParsedArgs
		{
			Target       = targetNode,
			TargetArgs   = targetArgs,
			SingleTarget = singleTarget,
		};

		return await DepGraphExecutor.ExecuteAsync(targetNode, parsed, _graph);
	}

	// ── Graph building ────────────────────────────────────────────────────────

	private void BuildGraph()
	{
		_typeNameToRoute = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var path in UnionReflector.GetCasePaths(typeof(TUnion)))
		{
			var defaultUnion = UnionReflector.ConstructDefault<TUnion>(path.CaseType, path.UnionCtor);
			var builder      = (UnionTargetBuilderImpl<TUnion>)_bind!(defaultUnion);

			var node = new TargetNode
			{
				Route       = path.Route,
				Kind        = builder.Kind,
				DtoType     = path.CaseType,
				Description = builder.DescriptionValue,
			};
			node.Hidden = builder.IsHidden;
			node.RouteRequires.AddRange(builder.DepTypeNames);
			node.RouteComposes.AddRange(builder.CompTypeNames);

			_graph.Targets.Add(node);
			_graph.ByRoute[string.Join("/", path.Route)] = node;
			_typeNameToRoute[path.CaseType.Name] = string.Join("/", path.Route);
		}
	}

	private void ResolveRouteDeps()
	{
		var map = _typeNameToRoute!;
		foreach (var node in _graph.Targets)
		{
			foreach (var depName in node.RouteRequires)
			{
				if (map.TryGetValue(depName, out var r) && _graph.ByRoute.TryGetValue(r, out var dep))
					node.RequiresResolved.Add(dep);
			}
			foreach (var compName in node.RouteComposes)
			{
				if (map.TryGetValue(compName, out var r) && _graph.ByRoute.TryGetValue(r, out var comp))
					node.ComposesResolved.Add(comp);
			}
		}
	}

	/// <summary>
	/// Sets <c>AsyncBody</c> on every node in the dependency plan.
	/// The root gets real <paramref name="targetArgs"/>; dep nodes get empty args (their
	/// case DTOs are constructed with defaults, so their Executes closures see default values).
	/// </summary>
	private void WireExecutionBodies(TargetNode root, string[] targetArgs)
	{
		foreach (var node in _graph.Targets)
		{
			var n    = node;
			var args = ReferenceEquals(n, root) ? targetArgs : Array.Empty<string>();
			n.AsyncBody = async () =>
			{
				if (_bind is null || n.DtoType is null) return;
				var caseInstance = Parsing.DtoBinder.Bind(n.DtoType, args);
				var realUnion    = UnionReflector.ConstructUnion<TUnion>(typeof(TUnion), n.DtoType, caseInstance, n.Route);
				var builder2     = (UnionTargetBuilderImpl<TUnion>)_bind!(realUnion);
				await builder2.ExecuteAsync();
			};
		}
	}

	// ── Argv helpers ──────────────────────────────────────────────────────────

	private (string[] remaining, bool single, bool help, bool version) ExtractGlobals(string[] argv)
	{
		var remaining = new List<string>();
		var single = false; var help = false; var version = false;
		var i = 0;
		while (i < argv.Length)
		{
			var arg = argv[i];
			if (arg is "-h" or "--help")           { help    = true; i++; continue; }
			if (arg == "--version")                { version = true; i++; continue; }
			if (arg is "-s" or "--single-target")  { single  = true; i++; continue; }

			var matched = false;
			foreach (var opt in _options)
			{
				var longN  = opt.Long.TrimStart('-');
				var shortN = opt.Short?.TrimStart('-');
				var argN   = arg.TrimStart('-');
				if (argN != longN && argN != shortN) continue;
				if (opt.IsFlag) { opt.Set("true"); i++; }
				else            { i++; if (i < argv.Length) opt.Set(argv[i++]); }
				matched = true;
				break;
			}
			if (!matched) { remaining.Add(arg); i++; }
		}
		return (remaining.ToArray(), single, help, version);
	}

	private (string routeKey, string[] targetArgs) ResolveRoute(string[] remaining)
	{
		var tokens = new List<string>(); var rest = new List<string>(); var done = false;
		foreach (var token in remaining)
		{
			if (!done && !token.StartsWith("-"))
			{
				var candidate = string.Join("/", [..tokens, token.ToLowerInvariant()]);
				if (_graph.ByRoute.ContainsKey(candidate))
					tokens.Add(token.ToLowerInvariant());
				else if (_graph.ByRoute.Keys.Any(k => k.StartsWith(candidate + "/", StringComparison.OrdinalIgnoreCase)))
					tokens.Add(token.ToLowerInvariant());
				else { done = true; rest.Add(token); }
			}
			else rest.Add(token);
		}
		return (string.Join("/", tokens), rest.ToArray());
	}

	private static object ParseRaw(Type t, string raw, string flagName)
	{
		var target = Nullable.GetUnderlyingType(t) ?? t;
		try
		{
			if (target == typeof(string))  return raw;
			if (target == typeof(int))     return int.Parse(raw);
			if (target == typeof(long))    return long.Parse(raw);
			if (target == typeof(double))  return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
			if (target == typeof(bool))    return bool.Parse(raw);
			if (target.IsEnum)             return Enum.Parse(target, raw, ignoreCase: true);
			return raw;
		}
		catch { throw new MakeException($"Cannot parse '{raw}' for '{flagName}'.", 2); }
	}
}
