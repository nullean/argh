using System.Reflection;

namespace Nullean.Make.UnionGraph;

/// <summary>Runtime reflection helpers for C# 15 union types in Make pipelines.</summary>
internal static class UnionReflector
{
	/// <summary>True when <paramref name="t"/> is a C# 15 union type (implements IUnion, carries [Union], or matches the structural pattern).</summary>
	internal static bool IsUnionType(Type t)
	{
		if (t.GetInterfaces().Any(i => i.Name == "IUnion"))
			return true;
		if (t.GetCustomAttributesData().Any(a => a.AttributeType.Name == "UnionAttribute"))
			return true;
		// Structural fallback: has object? Value property + ≥1 single-param public constructor
		var valueProp = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
		return valueProp is not null
		       && (valueProp.PropertyType == typeof(object) || valueProp.PropertyType == typeof(object))
		       && GetUnionCtors(t).Length > 0;
	}

	/// <summary>Returns all single-parameter public constructors; each parameter type is a union case type.</summary>
	internal static ConstructorInfo[] GetUnionCtors(Type t)
		=> t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
		    .Where(c => c.GetParameters().Length == 1)
		    .ToArray();

	/// <summary>
	/// Recursively enumerates all leaf case paths in the union hierarchy.
	/// Nested union case types become namespace prefix segments in the route.
	/// </summary>
	internal static List<CasePath> GetCasePaths(Type unionType)
	{
		var result = new List<CasePath>();
		Collect(unionType, [], result);
		return result;
	}

	private static void Collect(Type unionType, string[] prefix, List<CasePath> result)
	{
		foreach (var ctor in GetUnionCtors(unionType))
		{
			var caseType = ctor.GetParameters()[0].ParameterType;
			var segment  = ToKebabCase(caseType.Name);
			if (IsUnionType(caseType))
				Collect(caseType, [..prefix, segment], result);
			else
				result.Add(new CasePath([..prefix, segment], caseType, ctor));
		}
	}

	/// <summary>Constructs a union value wrapping a default instance of <paramref name="caseType"/>.</summary>
	internal static TUnion ConstructDefault<TUnion>(Type caseType, ConstructorInfo unionCtor)
		=> (TUnion)unionCtor.Invoke([CreateDefaultInstance(caseType)]);

	/// <summary>
	/// Constructs the outermost <typeparamref name="TUnion"/> by walking up the nested ctor chain
	/// from <paramref name="caseInstance"/> to the top of the union hierarchy.
	/// </summary>
	internal static TUnion ConstructUnion<TUnion>(Type outerUnionType, Type caseType, object caseInstance, string[] route)
		=> (TUnion)WrapValue(outerUnionType, caseType, caseInstance, route, 0);

	private static object WrapValue(Type unionType, Type leafCaseType, object leafInstance, string[] route, int depth)
	{
		foreach (var ctor in GetUnionCtors(unionType))
		{
			var paramType = ctor.GetParameters()[0].ParameterType;
			if (paramType == leafCaseType)
				return ctor.Invoke([leafInstance]);
			if (IsUnionType(paramType) && depth < route.Length - 1
			    && ToKebabCase(paramType.Name) == route[depth])
			{
				var inner = WrapValue(paramType, leafCaseType, leafInstance, route, depth + 1);
				return ctor.Invoke([inner]);
			}
		}
		throw new InvalidOperationException($"Cannot wrap {leafCaseType.Name} into {unionType.Name}.");
	}

	/// <summary>Builds a map of case-type simple name → full route key for fast dep resolution.</summary>
	internal static Dictionary<string, string> BuildTypeNameToRouteMap(Type unionType)
	{
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var path in GetCasePaths(unionType))
			map[path.CaseType.Name] = string.Join("/", path.Route);
		return map;
	}

	private static object CreateDefaultInstance(Type t)
	{
		if (t.IsValueType) return Activator.CreateInstance(t)!;
		var ctor = t.GetConstructors()
			.OrderByDescending(c => c.GetParameters().Length)
			.FirstOrDefault(c => c.GetParameters().All(p => p.HasDefaultValue));
		if (ctor is not null)
			return ctor.Invoke(ctor.GetParameters().Select(p => p.DefaultValue).ToArray());
		return Activator.CreateInstance(t)!;
	}

	internal static string ToKebabCase(string name)
	{
		if (string.IsNullOrEmpty(name)) return name;
		var sb = new System.Text.StringBuilder();
		for (var i = 0; i < name.Length; i++)
		{
			var c = name[i];
			if (char.IsUpper(c) && i > 0) sb.Append('-');
			sb.Append(char.ToLowerInvariant(c));
		}
		return sb.ToString();
	}
}

/// <summary>A leaf path in the union case hierarchy: the CLI route, the record case type, and the wrapping constructor.</summary>
internal sealed record CasePath(string[] Route, Type CaseType, ConstructorInfo UnionCtor);
