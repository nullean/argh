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
/// var app = new MakeApp&lt;BuildTarget&gt;("my-build");
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
public sealed class MakeApp<TUnion> where TUnion : struct
{
	private readonly BuildGraph _graph = new();
	private readonly List<UnionOptionDecl> _options = new();
	private Func<TUnion, IUnionTargetBuilder<TUnion>>? _bind;
	private Dictionary<string, string>? _typeNameToRoute;
	private readonly Dictionary<TargetNode, UnionCase<TUnion>> _caseByNode = new();

	public MakeApp(string name, string? description = null)
	{
		_graph.AppName = name;
		_graph.AppDescription = description;
	}

	// ── Global option registration ────────────────────────────────────────────

	/// <summary>Registers a boolean flag. Read the returned ref's <c>Value</c> inside target bodies.</summary>
	public UnionOptionRef<bool> Flag(string longName, string? shortName = null, string? description = null)
	{
		var r = new UnionOptionRef<bool>(
			longName,
			shortName,
			description,
			false,
			s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1"
		);
		_options.Add(new UnionOptionDecl(longName, shortName, description, IsFlag: true, Set: r.Set));
		_graph.GlobalOptions.Add(new GlobalOptionNode { Long = longName, Short = shortName, Description = description, IsFlag = true });
		return r;
	}

	/// <summary>Registers a typed option. Read the returned ref's <c>Value</c> inside target bodies.</summary>
	public UnionOptionRef<T> Option<T>(string longName, string? description = null, T defaultValue = default!)
	{
		var r = new UnionOptionRef<T>(longName, null, description, defaultValue, raw => (T)ParseRaw(typeof(T), raw, longName));
		_options.Add(new UnionOptionDecl(longName, null, description, IsFlag: false, Set: r.Set));
		_graph.GlobalOptions.Add(new GlobalOptionNode { Long = longName, Description = description, IsFlag = false, ValueType = typeof(T) });
		return r;
	}

	// ── Target / Command factories (used inside Bind) ─────────────────────────

	/// <summary>Creates a target builder for use inside <see cref="Bind"/>.</summary>
	public IUnionTargetBuilder<TUnion> Target() => new UnionTargetBuilderImpl<TUnion>(TargetKind.Target);

	/// <summary>Creates a command builder for use inside <see cref="Bind"/>. Commands compose other targets.</summary>
	public IUnionTargetBuilder<TUnion> Command() => new UnionTargetBuilderImpl<TUnion>(TargetKind.Command);

	/// <summary>A target whose body is <paramref name="body"/>. Its description comes from the case's XML doc comment.</summary>
	public IUnionTargetBuilder<TUnion> Target(Action body) => Target().Executes(body);

	/// <inheritdoc cref="Target(Action)"/>
	public IUnionTargetBuilder<TUnion> Target(Func<Task> body) => Target().Executes(body);

	/// <summary>A target that runs <paramref name="body"/> after <paramref name="dependsOn"/> (skipped under <c>-s</c>).</summary>
	public IUnionTargetBuilder<TUnion> Target(TUnion[] dependsOn, Action body) => Target().DependsOn(dependsOn).Executes(body);

	/// <inheritdoc cref="Target(TUnion[], Action)"/>
	public IUnionTargetBuilder<TUnion> Target(TUnion[] dependsOn, Func<Task> body) => Target().DependsOn(dependsOn).Executes(body);

	/// <summary>
	/// A command: runs <paramref name="requires"/> (skipped under <c>-s</c>), then <paramref name="composes"/>
	/// (always), then the optional <paramref name="body"/>.
	/// </summary>
	public IUnionTargetBuilder<TUnion> Command(TUnion[] requires, TUnion[] composes, Action? body = null)
	{
		var builder = Command().DependsOn(requires).Composes(composes);
		return body is null ? builder : builder.Executes(body);
	}

	// ── Bind ──────────────────────────────────────────────────────────────────

	/// <summary>
	/// Provides the exhaustive mapping from union case to target definition.
	/// <para>
	/// The lambda is called twice per relevant case: once at graph-build time with default case
	/// values (for metadata: deps, description, kind; a parameter without a default gets <c>default</c>) and once at execution time with real
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
		if (_bind is null)
		{
			Console.Error.WriteLine("[make] Bind() was not called.");
			return 1;
		}

		try
		{
			BuildGraph(UnionCaseTable<TUnion>.Required);
			ResolveRouteDeps();
			GraphValidator.Validate(_graph);
			_graph.Source = new UnionTargetSource<TUnion>(_bind, _caseByNode, ResolveCase);
		}
		catch (MakeException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return ex.ExitCode;
		}

		if (Wrappers.MakeIntrinsics.TryRun(args, _graph) is { } intrinsicExit)
			return intrinsicExit;

		var scriptName = _graph.AppName;

		if (args.Length == 0)
		{
			MakeHelpPrinter.PrintRoot(_graph, scriptName);
			return 0;
		}

		string[] remaining;
		bool singleTarget, showHelp, showVersion;
		GraphFormat? graphFormat;
		try
		{
			(remaining, singleTarget, showHelp, showVersion, graphFormat) = ExtractGlobals(args);
		}
		catch (MakeException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return ex.ExitCode;
		}

		if (showVersion)
		{
			Console.WriteLine(MakeVersion.OfEntryAssembly());
			return 0;
		}

		var (routeKey, targetArgs) = ResolveRoute(remaining);

		if (showHelp)
		{
			if (_graph.ByRoute.TryGetValue(routeKey, out var helpNode))
			{
				if (helpNode.Kind == TargetKind.Command)
					MakeHelpPrinter.PrintCommand(helpNode, _graph, scriptName);
				else
					MakeHelpPrinter.PrintTarget(helpNode, _graph, scriptName);
			}
			else if (string.IsNullOrEmpty(routeKey) || !MakeHelpPrinter.PrintNamespace(routeKey, _graph, scriptName))
				MakeHelpPrinter.PrintRoot(_graph, scriptName);
			return 0;
		}

		if (string.IsNullOrEmpty(routeKey))
		{
			var unknown = remaining.FirstOrDefault(t => !t.StartsWith("-"));
			if (unknown is not null)
			{
				Console.Error.WriteLine($"Unknown target '{unknown}'.");
				return 2;
			}
			if (graphFormat is { } format)
				return DepGraphExecutor.DrawWholeGraph(_graph, format);
			MakeHelpPrinter.PrintRoot(_graph, scriptName);
			return 0;
		}

		if (!_graph.ByRoute.TryGetValue(routeKey, out var targetNode))
		{
			Console.Error.WriteLine($"Unknown target '{routeKey}'.");
			return 2;
		}

		var parsed = new ParsedArgs { Target = targetNode, TargetArgs = targetArgs, SingleTarget = singleTarget, Graph = graphFormat };

		return await DepGraphExecutor.ExecuteAsync(targetNode, parsed, _graph);
	}

	// ── Graph building ────────────────────────────────────────────────────────

	private void BuildGraph(IReadOnlyList<UnionCase<TUnion>> cases)
	{
		_typeNameToRoute = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var path in cases)
		{
			var builder = (UnionTargetBuilderImpl<TUnion>)_bind!(path.CreateDefault());

			var node = new TargetNode
			{
				Route = path.Route,
				Kind = builder.Kind,
				DtoType = path.CaseType,
				Description = builder.DescriptionValue ?? Docs.DocDescriptions.ForType(path.CaseType),
			};
			node.Hidden = builder.IsHidden;
			node.RouteRequires.AddRange(builder.Deps.Select(d => d.TypeName));
			node.RouteComposes.AddRange(builder.Comps.Select(d => d.TypeName));

			_graph.Targets.Add(node);
			_caseByNode[node] = path;
			_graph.ByRoute[string.Join("/", path.Route)] = node;
			_typeNameToRoute[path.CaseType.Name] = string.Join("/", path.Route);
		}
	}

	private void ResolveRouteDeps()
	{
		foreach (var node in _graph.Targets)
		{
			foreach (var depName in node.RouteRequires)
				node.RequiresResolved.Add(ResolveCase(node, depName));
			foreach (var compName in node.RouteComposes)
				node.ComposesResolved.Add(ResolveCase(node, compName));
		}
	}

	private TargetNode ResolveCase(TargetNode from, string caseTypeName)
	{
		if (_typeNameToRoute!.TryGetValue(caseTypeName, out var r) && _graph.ByRoute.TryGetValue(r, out var target))
			return target;
		var name = caseTypeName.Length == 0 ? "an empty union value" : $"'{caseTypeName}'";
		throw new MakeException($"Target '{string.Join("/", from.Route)}' depends on {name}, which is not a case of {typeof(TUnion).Name}.", 1);
	}

	// ── Argv helpers ──────────────────────────────────────────────────────────

	private (string[] remaining, bool single, bool help, bool version, GraphFormat? graph) ExtractGlobals(string[] argv)
	{
		var remaining = new List<string>();
		var single = false;
		var help = false;
		var version = false;
		GraphFormat? graph = null;
		var i = 0;
		while (i < argv.Length)
		{
			var arg = argv[i];
			if (arg is "-h" or "--help")
			{
				help = true;
				i++;
				continue;
			}
			if (arg == "--version")
			{
				version = true;
				i++;
				continue;
			}
			if (arg is "-s" or "--single-target")
			{
				single = true;
				i++;
				continue;
			}
			if (GraphOption.TryRead(argv, ref i) is { } format)
			{
				graph = format;
				continue;
			}

			var matched = false;
			foreach (var opt in _options)
			{
				var longN = opt.Long.TrimStart('-');
				var shortN = opt.Short?.TrimStart('-');
				var argN = arg.TrimStart('-');
				if (argN != longN && argN != shortN)
					continue;
				if (opt.IsFlag)
				{
					opt.Set("true");
					i++;
				}
				else
				{
					i++;
					if (i >= argv.Length)
						throw new MakeException($"Missing value for '{opt.Long}'.", 2);
					opt.Set(argv[i++]);
				}
				matched = true;
				break;
			}
			if (!matched)
			{
				remaining.Add(arg);
				i++;
			}
		}
		return (remaining.ToArray(), single, help, version, graph);
	}

	private (string routeKey, string[] targetArgs) ResolveRoute(string[] remaining)
	{
		var tokens = new List<string>();
		var rest = new List<string>();
		var done = false;
		foreach (var token in remaining)
		{
			if (!done && !token.StartsWith("-"))
			{
				var candidate = string.Join("/", [.. tokens, token.ToLowerInvariant()]);
				if (_graph.ByRoute.ContainsKey(candidate))
					tokens.Add(token.ToLowerInvariant());
				else if (_graph.ByRoute.Keys.Any(k => k.StartsWith(candidate + "/", StringComparison.OrdinalIgnoreCase)))
					tokens.Add(token.ToLowerInvariant());
				else
				{
					done = true;
					rest.Add(token);
				}
			}
			else
				rest.Add(token);
		}
		return (string.Join("/", tokens), rest.ToArray());
	}

	private static object ParseRaw(Type t, string raw, string flagName)
	{
		var target = Nullable.GetUnderlyingType(t) ?? t;
		try
		{
			if (target == typeof(string))
				return raw;
			if (target == typeof(int))
				return int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
			if (target == typeof(long))
				return long.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
			if (target == typeof(double))
				return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
			if (target == typeof(bool))
				return bool.Parse(raw);
			if (target.IsEnum)
				return Enum.Parse(target, raw, ignoreCase: true);
			return raw;
		}
		catch
		{
			throw new MakeException($"Cannot parse '{raw}' for '{flagName}'.", 2);
		}
	}
}
