using System.Reflection;
using Nullean.Argh;

namespace Nullean.Make.Discovery;

/// <summary>One option or positional of a per-target DTO, as the CLI sees it.</summary>
/// <param name="Name">Kebab-case CLI name, without dashes.</param>
/// <param name="Type">The constructor parameter's type.</param>
/// <param name="IsPositional">True for an <see cref="ArgumentAttribute"/> parameter.</param>
/// <param name="IsRest">True for a parameter that takes everything after <c>--</c> (F# <c>[&lt;Rest&gt;]</c>).</param>
/// <param name="HasDefault">True when the parameter has a default value.</param>
/// <param name="Default">The default value; only meaningful when <paramref name="HasDefault"/> is true.</param>
internal sealed record DtoOption(string Name, Type Type, bool IsPositional, bool IsRest, bool HasDefault, object? Default)
{
	/// <summary>True for <c>bool</c> and <c>bool?</c>, which bind as <c>--name</c> / <c>--no-name</c> switches.</summary>
	public bool IsSwitch => Type == typeof(bool) || Type == typeof(bool?);
}

/// <summary>
/// Reads a per-target DTO's options from its widest constructor, the same way <see cref="Parsing.DtoBinder"/> binds them.
/// Works for C# records, union case records and F# records.
/// </summary>
internal static class DtoOptions
{
	public static IReadOnlyList<DtoOption> Describe(Type dtoType)
	{
		var ctor = dtoType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
		return ctor.GetParameters()
			.Select(p => new DtoOption(
				ToKebabCase(p.Name ?? "arg"),
				p.ParameterType,
				p.GetCustomAttribute<ArgumentAttribute>() is not null,
				// RestAttribute lives in Nullean.Make.Fs, which this project cannot reference.
				p.GetCustomAttributes().Any(a => a.GetType().Name == "RestAttribute"),
				p.HasDefaultValue,
				p.HasDefaultValue ? p.DefaultValue : null))
			.ToList();
	}

	/// <summary>The type name help prints: C# keywords (<c>int</c>, not <c>int32</c>), F# <c>option&lt;T&gt;</c> as <c>T?</c>.</summary>
	public static string FriendlyTypeName(Type t)
	{
		// Unwrap F# option<T> → "T?"
		if (t.IsGenericType && t.GetGenericTypeDefinition().Name == "FSharpOption`1")
			return FriendlyTypeName(t.GetGenericArguments()[0]) + "?";
		if (t.IsGenericType && t.GetGenericTypeDefinition().Name == "FSharpList`1")
			return FriendlyTypeName(t.GetGenericArguments()[0]) + "...";
		var u = Nullable.GetUnderlyingType(t) ?? t;
		// C# keyword names, as Argh help prints them: "int", not "int32".
		if (u == typeof(int)) return "int";
		if (u == typeof(long)) return "long";
		if (u == typeof(bool)) return "bool";
		if (u == typeof(float)) return "float";
		if (u == typeof(double)) return "double";
		if (u == typeof(decimal)) return "decimal";
		if (u == typeof(string)) return "string";
		return u.Name.ToLowerInvariant();
	}

	private static string ToKebabCase(string name)
	{
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
