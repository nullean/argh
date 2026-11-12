using System.Collections;
using System.Globalization;
using Nullean.Make.Discovery;

namespace Nullean.Make.Execution;

/// <summary>Formats a target's value as the fields that differ from its default value: <c>filter: Unit, retries: 2</c>.</summary>
internal static class ValueDisplay
{
	public static string? Differences(object? value, object? defaults)
	{
		if (value is null || Equals(value, defaults))
			return null;

		var type = value.GetType();
		if (type.GetConstructors().Length == 0)
			return null;

		var parts = new List<string>();
		foreach (var option in DtoOptions.Describe(type))
		{
			var property = type.GetProperties().FirstOrDefault(
				p => string.Equals(p.Name.Replace("_", ""), option.Name.Replace("-", ""), StringComparison.OrdinalIgnoreCase)
			);
			if (property is null || property.GetIndexParameters().Length > 0)
				continue;
			var current = property.GetValue(value);
			var original = defaults is not null && defaults.GetType() == type ? property.GetValue(defaults) : null;
			if (Equals(current, original))
				continue;
			parts.Add($"{option.Name}: {Format(current)}");
		}
		return parts.Count == 0 ? null : string.Join(", ", parts);
	}

	/// <summary>A value as a user wrote it: F# <c>Some x</c> as <c>x</c>, <c>None</c> and <c>null</c> as <c>none</c>, lists comma-separated.</summary>
	internal static string Format(object? value)
	{
		switch (value)
		{
			case null:
				return "none";
			case string s:
				return s;
			case IFormattable f:
				return f.ToString(null, CultureInfo.InvariantCulture);
		}

		var type = value.GetType();
		if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "FSharpOption`1")
			return Format(type.GetProperty("Value")!.GetValue(value));
		if (value is IEnumerable items)
			return string.Join(",", items.Cast<object?>().Select(Format));
		return value.ToString() ?? "";
	}

	/// <summary>The route as typed, with the value after it in parentheses when it is not the default.</summary>
	public static string Name(TargetNode node, string? value) =>
		string.IsNullOrEmpty(value) ? string.Join(" ", node.Route) : $"{string.Join(" ", node.Route)} ({value})";
}
