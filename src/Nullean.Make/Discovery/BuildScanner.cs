using System.Reflection;

namespace Nullean.Make.Discovery;

internal static class BuildScanner
{
	private static readonly Type _targetType = typeof(Target);
	private static readonly Type _commandType = typeof(Command);
	private static readonly Type _targetOpenGeneric = typeof(Target<>);
	private static readonly Type _commandOpenGeneric = typeof(Command<>);
	private static readonly Type _namespaceType = typeof(Namespace);

	public static BuildGraph Scan<TBuild>(TBuild build) where TBuild : MakeBuild
	{
		var graph = new BuildGraph();
		graph.AppName = typeof(TBuild).Name.ToLowerInvariant();

		ScanGlobalOptions(typeof(TBuild), graph);
		ScanTargets(build, typeof(TBuild), graph, Array.Empty<string>());
		ScanNestedNamespaces(typeof(TBuild), graph);
		ResolveDepGraph(graph);

		return graph;
	}

	private static void ScanGlobalOptions(Type type, BuildGraph graph)
	{
		foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
		{
			var globalOpt = prop.GetCustomAttribute<GlobalOptionAttribute>();
			if (globalOpt is not null)
			{
				graph.GlobalOptions.Add(new GlobalOptionNode
				{
					Property = prop,
					Long = globalOpt.Long,
					Short = globalOpt.Short,
					Description = globalOpt.Description,
					IsFlag = false
				});
				continue;
			}

			var flag = prop.GetCustomAttribute<FlagAttribute>();
			if (flag is not null)
			{
				graph.GlobalOptions.Add(new GlobalOptionNode
				{
					Property = prop,
					Long = flag.Long,
					Short = flag.Short,
					Description = flag.Description,
					IsFlag = true
				});
			}
		}
	}

	private static void ScanTargets(object instance, Type type, BuildGraph graph, string[] prefix)
	{
		foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
		{
			var propType = prop.PropertyType;

			// Plain Target delegate
			if (propType == _targetType)
			{
				var value = (Target?)prop.GetValue(instance);
				if (value is null) continue;
				var route = BuildRoute(prefix, prop);
				var node = new TargetNode { Route = route, Kind = TargetKind.Target, ConfigureMethod = value.Method };
				value(new TargetBuilderImpl(node));
				Register(graph, node);
				continue;
			}

			// Plain Command delegate
			if (propType == _commandType)
			{
				var value = (Command?)prop.GetValue(instance);
				if (value is null) continue;
				var route = BuildRoute(prefix, prop);
				var node = new TargetNode { Route = route, Kind = TargetKind.Command, ConfigureMethod = value.Method };
				value(new TargetBuilderImpl(node));
				Register(graph, node);
				continue;
			}

			// Target<T> delegate
			if (propType.IsGenericType && propType.GetGenericTypeDefinition() == _targetOpenGeneric)
			{
				var dtoType = propType.GetGenericArguments()[0];
				var value = (Delegate?)prop.GetValue(instance);
				if (value is null) continue;
				var route = BuildRoute(prefix, prop);
				var node = new TargetNode { Route = route, Kind = TargetKind.Target, DtoType = dtoType, ConfigureMethod = value.Method };
				InvokeTypedDelegate(value, dtoType, node, TargetKind.Target);
				Register(graph, node);
				continue;
			}

			// Command<T> delegate
			if (propType.IsGenericType && propType.GetGenericTypeDefinition() == _commandOpenGeneric)
			{
				var dtoType = propType.GetGenericArguments()[0];
				var value = (Delegate?)prop.GetValue(instance);
				if (value is null) continue;
				var route = BuildRoute(prefix, prop);
				var node = new TargetNode { Route = route, Kind = TargetKind.Command, DtoType = dtoType, ConfigureMethod = value.Method };
				InvokeTypedDelegate(value, dtoType, node, TargetKind.Command);
				Register(graph, node);
				continue;
			}
		}
	}

	private static void InvokeTypedDelegate(Delegate value, Type dtoType, TargetNode node, TargetKind kind)
	{
		var builderType = typeof(TargetBuilderImplOfT<>).MakeGenericType(dtoType);
		var builder = (ITargetBuilder)builderType
			.GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, [typeof(TargetNode)], null)!
			.Invoke([node]);
		value.DynamicInvoke(builder);
	}

	/// <summary>Auto-discovers public nested classes inheriting <see cref="Namespace"/> and registers their targets.</summary>
	private static void ScanNestedNamespaces(Type buildType, BuildGraph graph)
	{
		foreach (var nested in buildType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
		{
			if (!_namespaceType.IsAssignableFrom(nested) || nested == _namespaceType)
				continue;

			var segment = ToKebabCase(nested.Name);
			if (graph.Targets.Any(t => t.Route.Length > 0 && t.Route[0] == segment))
				continue;

			var nsInstance = Activator.CreateInstance(nested);
			if (nsInstance is null)
				continue;

			ScanTargets(nsInstance, nested, graph, [segment]);
		}
	}

	private static string[] BuildRoute(string[] prefix, PropertyInfo prop)
	{
		var leaf = ToKebabCase(prop.Name);
		return [..prefix, leaf];
	}

	private static void Register(BuildGraph graph, TargetNode node)
	{
		graph.Targets.Add(node);
		graph.ByRoute[string.Join("/", node.Route)] = node;
		if (node.ConfigureMethod is not null)
			graph.ByMethod[node.ConfigureMethod] = node;
	}

	private static void ResolveDepGraph(BuildGraph graph)
	{
		foreach (var node in graph.Targets)
		{
			foreach (var method in node.Requires)
			{
				if (graph.ByMethod.TryGetValue(method, out var dep))
					node.RequiresResolved.Add(dep);
			}

			foreach (var method in node.Composes)
			{
				if (graph.ByMethod.TryGetValue(method, out var dep))
					node.ComposesResolved.Add(dep);
			}
		}
	}

	internal static string ToKebabCase(string name)
	{
		if (string.IsNullOrEmpty(name)) return name;
		var sb = new System.Text.StringBuilder();
		for (var i = 0; i < name.Length; i++)
		{
			var c = name[i];
			if (char.IsUpper(c) && i > 0)
				sb.Append('-');
			sb.Append(char.ToLowerInvariant(c));
		}
		return sb.ToString();
	}
}
