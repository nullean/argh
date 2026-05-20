using System.Reflection;
using Nullean.Argh;
using Nullean.Argh.Matching;

namespace Nullean.Make.Parsing;

/// <summary>Reflection-based DTO binder for per-target argument records. Handles primary constructor parameters (PascalCase → kebab-case) and [Argument] positionals.</summary>
internal static class DtoBinder
{
	public static object Bind(Type dtoType, string[] args)
	{
		var ctor = dtoType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
		var ctorParams = ctor.GetParameters();

		if (ctorParams.Length == 0)
			return Activator.CreateInstance(dtoType)!;

		var positionals = new List<ParameterInfo>();
		var flags = new Dictionary<string, ParameterInfo>(StringComparer.OrdinalIgnoreCase);

		foreach (var p in ctorParams)
		{
			var isPositional = p.GetCustomAttribute<ArgumentAttribute>() is not null;
			if (isPositional)
				positionals.Add(p);
			else
			{
				var longName = ToKebabCase(p.Name ?? p.Position.ToString());
				flags[longName] = p;
			}
		}

		var boundValues = new object?[ctorParams.Length];

		// Fill defaults first
		for (var i = 0; i < ctorParams.Length; i++)
		{
			if (ctorParams[i].HasDefaultValue)
				boundValues[i] = ctorParams[i].DefaultValue;
		}

		var positionalIndex = 0;
		var argIndex = 0;

		while (argIndex < args.Length)
		{
			var arg = args[argIndex];

			if (arg.StartsWith("--") || (arg.StartsWith("-") && arg.Length == 2))
			{
				var flagName = arg.TrimStart('-');
				// Handle --no-xxx as bool false, --xxx as bool true
				var negated = false;
				if (flagName.StartsWith("no-"))
				{
					negated = true;
					flagName = flagName.Substring(3);
				}

				if (!flags.TryGetValue(flagName, out var param))
				{
					// Levenshtein suggestion
					var suggestions = FuzzyMatch.FindClosest(flagName, flags.Keys, 3);
					var hint = suggestions.Count > 0 ? $" Did you mean --{suggestions[0]}?" : "";
					throw new MakeException($"Unknown flag '--{flagName}'.{hint}", 2);
				}

				var paramIndex = param.Position;

				if (param.ParameterType == typeof(bool) || param.ParameterType == typeof(bool?))
				{
					// --flag or --no-flag; next token might also be true/false
					if (!negated && argIndex + 1 < args.Length &&
						bool.TryParse(args[argIndex + 1], out var boolVal))
					{
						boundValues[paramIndex] = boolVal;
						argIndex += 2;
					}
					else
					{
						boundValues[paramIndex] = !negated;
						argIndex++;
					}
				}
				else
				{
					argIndex++;
					if (argIndex >= args.Length)
						throw new MakeException($"Flag '--{flagName}' requires a value.", 2);

					boundValues[paramIndex] = ParseValue(param.ParameterType, args[argIndex], flagName);
					argIndex++;
				}
			}
			else
			{
				// Positional
				if (positionalIndex < positionals.Count)
				{
					var param = positionals[positionalIndex++];
					boundValues[param.Position] = ParseValue(param.ParameterType, arg, param.Name ?? "arg");
				}
				argIndex++;
			}
		}

		return ctor.Invoke(boundValues);
	}

	private static object ParseValue(Type type, string raw, string paramName)
	{
		var target = Nullable.GetUnderlyingType(type) ?? type;

		if (raw is null)
			return null!;

		try
		{
			if (target == typeof(string)) return raw;
			if (target == typeof(int)) return int.Parse(raw);
			if (target == typeof(long)) return long.Parse(raw);
			if (target == typeof(double)) return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
			if (target == typeof(bool)) return bool.Parse(raw);
			if (target == typeof(FileInfo)) return new FileInfo(raw);
			if (target == typeof(DirectoryInfo)) return new DirectoryInfo(raw);
			if (target == typeof(Uri)) return new Uri(raw);
			if (target == typeof(TimeSpan)) return TimeSpan.Parse(raw);
			if (target.IsEnum) return Enum.Parse(target, raw, ignoreCase: true);
			throw new MakeException($"Unsupported parameter type '{type.Name}' for '{paramName}'.", 2);
		}
		catch (MakeException) { throw; }
		catch (Exception ex)
		{
			throw new MakeException($"Cannot parse '{raw}' as {target.Name} for '--{paramName}': {ex.Message}", 2);
		}
	}

	private static string ToKebabCase(string name)
	{
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

