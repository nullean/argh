using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Nullean.Argh;

public sealed partial class CliParserGenerator
{
	// ── Union help rows ──────────────────────────────────────────────────────

	/// <summary>
	/// Returns the command's option rows for help, with each union parameter's case properties expanded into
	/// rows of their own: after the selector flag in flag mode (<c>--json-pretty</c>), appended at the end in
	/// argument mode (<c>--pretty</c>, shared by every case that declares it).
	/// </summary>
	private static List<ParameterModel> ExpandUnionHelpRows(IEnumerable<ParameterModel> commandFlags, ImmutableArray<ParameterModel> allParameters)
	{
		var rows = new List<ParameterModel>();
		foreach (var p in commandFlags)
		{
			rows.Add(p);
			if (p.ScalarKind == CliScalarKind.Union && !p.UnionIsArgument)
				AddUnionCasePropRows(rows, p);
		}

		foreach (var p in allParameters)
		{
			if (p.Kind == ParameterKind.Positional && p.ScalarKind == CliScalarKind.Union && p.UnionIsArgument)
				AddUnionCasePropRows(rows, p);
		}

		return rows;
	}

	private static void AddUnionCasePropRows(List<ParameterModel> rows, ParameterModel union)
	{
		if (union.UnionCases.IsDefaultOrEmpty)
			return;

		if (!union.UnionIsArgument)
		{
			foreach (var c in union.UnionCases)
				foreach (var prop in c.Properties)
					rows.Add(UnionCasePropHelpRow(prop.FlagModeName, prop, $"Applies when --{union.CliLongName} is {c.CliName}."));
			return;
		}

		// Argument mode: one row per distinct prop name, listing every case that declares it.
		var order = new List<string>();
		var byName = new Dictionary<string, (UnionCasePropInfo Prop, List<string> Cases)>(StringComparer.OrdinalIgnoreCase);
		foreach (var c in union.UnionCases)
		{
			foreach (var prop in c.Properties)
			{
				if (!byName.TryGetValue(prop.CliName, out var entry))
				{
					entry = (prop, new List<string>());
					byName[prop.CliName] = entry;
					order.Add(prop.CliName);
				}
				entry.Cases.Add(c.CliName);
			}
		}

		foreach (var name in order)
		{
			var (prop, cases) = byName[name];
			var caseList = cases.Count == 1
				? cases[0]
				: string.Join(", ", cases.Take(cases.Count - 1)) + " or " + cases[cases.Count - 1];
			rows.Add(UnionCasePropHelpRow(prop.CliName, prop, $"Applies when <{union.CliLongName}> is {caseList}."));
		}
	}

	private static bool UnionBoolDefaultsTrue(UnionCasePropInfo prop) =>
		prop.Special == BoolSpecialKind.Bool && prop.DefaultValueLiteral == "true";

	/// <summary>The default as help shows it: no <c>f</c>/<c>m</c> literal suffix.</summary>
	private static string? UnionCasePropHelpDefault(UnionCasePropInfo prop) =>
		prop.DefaultValueLiteral is { Length: > 1 } d && prop.TypeName is "float" or "decimal" && (d.EndsWith("f") || d.EndsWith("m"))
			? d.Substring(0, d.Length - 1)
			: prop.DefaultValueLiteral;

	private static ParameterModel UnionCasePropHelpRow(string cliName, UnionCasePropInfo prop, string description) =>
		new(
			prop.Name,
			prop.Name,
			cliName,
			ParameterKind.Flag,
			// Shown as --[no-]{flag}, the only way to switch it off.
			UnionBoolDefaultsTrue(prop) ? BoolSpecialKind.NullableBool : prop.Special,
			prop.ScalarKind,
			prop.TypeName,
			EnumTypeFq: prop.EnumTypeFq,
			EnumMemberNames: prop.EnumMemberNames.IsDefault ? ImmutableArray<string>.Empty : prop.EnumMemberNames,
			ParserTypeFq: null,
			CustomValueTypeFq: null,
			IsRequired: false,
			DefaultValueLiteral: UnionCasePropHelpDefault(prop),
			Description: UnionBoolDefaultsTrue(prop) && description.Length > 0 ? description + " [default: true]" : description,
			ShortOpt: null,
			Aliases: ImmutableArray<string>.Empty,
			EnumMemberCliNames: prop.EnumMemberCliNames,
			IsUnionCaseProp: true);

	private static string UnionCaseChoices(ParameterModel p) =>
		"<" + string.Join("|", p.UnionCases.Select(static c => c.CliName)) + ">";

	// ── Union case property types ────────────────────────────────────────────

	private static bool IsSupportedUnionCasePropType(CliScalarKind sk, BoolSpecialKind bs, string typeName, ITypeSymbol type)
	{
		if (bs == BoolSpecialKind.Bool)
			return true;
		if (type is not INamedTypeSymbol)
			return false;
		if (bs != BoolSpecialKind.None || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
			return false;
		if (sk == CliScalarKind.Enum)
			return true;
		return sk == CliScalarKind.Primitive && typeName is "string" or "int" or "long" or "float" or "double" or "decimal";
	}

	/// <summary>
	/// A C# literal for the case property's default, valid in a typed local declaration: enum members by name
	/// (<c>global::App.Level.Info</c>), <c>f</c>/<c>m</c> suffixes on float and decimal. Null when the property has no default.
	/// </summary>
	private static string? UnionCasePropDefaultLiteral(IParameterSymbol p, CliScalarKind sk, BoolSpecialKind bs, string typeName, string? enumFq)
	{
		if (!p.HasExplicitDefaultValue)
			return bs == BoolSpecialKind.Bool ? "false" : null;
		var v = p.ExplicitDefaultValue;
		if (v is null)
			return p.Type.IsReferenceType ? "null" : "default";
		if (sk == CliScalarKind.Enum && enumFq is not null && p.Type is INamedTypeSymbol enumType)
		{
			foreach (var m in enumType.GetMembers())
			{
				if (m is IFieldSymbol { HasConstantValue: true } f && Equals(f.ConstantValue, v))
					return enumFq + "." + f.Name;
			}
			return $"({enumFq})({SymbolDisplay.FormatPrimitive(v, quoteStrings: false, useHexadecimalNumbers: false)})";
		}
		var lit = SymbolDisplay.FormatPrimitive(v, quoteStrings: true, useHexadecimalNumbers: false);
		return typeName switch
		{
			"float" => lit + "f",
			"decimal" => lit + "m",
			_ => lit
		};
	}

	/// <summary>Reports AGH0037 for each union case property whose type union case flags cannot bind.</summary>
	private static void ReportUnsupportedUnionCasePropsAcc(DiagnosticAccumulator acc, Location location, ImmutableArray<ParameterModel> parameters)
	{
		foreach (var p in parameters)
		{
			if (p.ScalarKind != CliScalarKind.Union || p.UnionCases.IsDefaultOrEmpty)
				continue;
			foreach (var c in p.UnionCases)
				foreach (var prop in c.Properties)
					if (prop.UnsupportedTypeDisplay is { } t)
						acc.Add(UnionCasePropTypeNotSupported, location, p.SymbolName, $"'{c.Name}.{prop.Name}' ({t})");
		}
	}

	// ── AGH0036: union flag name collisions ──────────────────────────────────

	/// <summary>
	/// Reports AGH0036 when a union parameter's case names or case-property flags collide: two cases with the
	/// same CLI name, a case flag that another flag of the command already claims, or (argument mode) one bare
	/// flag name that two cases declare with different types.
	/// </summary>
	private static void ReportUnionFlagCollisionsAcc(DiagnosticAccumulator acc, Location location, ImmutableArray<ParameterModel> parameters)
	{
		var hasUnion = false;
		foreach (var p in parameters)
		{
			if (p.ScalarKind == CliScalarKind.Union && !p.UnionCases.IsDefaultOrEmpty)
			{
				hasUnion = true;
				break;
			}
		}
		if (!hasUnion)
			return;

		// Names claimed by the command's other flags, keyed to the owning parameter's symbol name.
		var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		void claim(string name, string owner)
		{
			if (!string.IsNullOrEmpty(name) && !claimed.ContainsKey(name))
				claimed[name] = owner;
		}

		foreach (var p in parameters)
		{
			if (p.Kind != ParameterKind.Flag || p.ScalarKind == CliScalarKind.Union)
				continue;
			claim(p.CliLongName, p.SymbolName);
			foreach (var al in p.Aliases)
				claim(al, p.SymbolName);
			if (p.Special == BoolSpecialKind.NullableBool)
				claim("no-" + p.CliLongName, p.SymbolName);
		}

		foreach (var p in parameters)
		{
			if (p.ScalarKind == CliScalarKind.Union && !p.UnionIsArgument && p.Kind == ParameterKind.Flag)
				claim(p.CliLongName, p.SymbolName);
		}

		foreach (var p in parameters)
		{
			if (p.ScalarKind != CliScalarKind.Union || p.UnionCases.IsDefaultOrEmpty)
				continue;

			var caseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var c in p.UnionCases)
			{
				if (!caseNames.Add(c.CliName))
					acc.Add(UnionFlagCollision, location, p.SymbolName, $"case '{c.CliName}'");
			}

			// Bare prop names in argument mode may repeat across cases when the type matches; the active case picks.
			var ownPropTypes = new Dictionary<string, (BoolSpecialKind Special, string TypeName)>(StringComparer.OrdinalIgnoreCase);
			foreach (var c in p.UnionCases)
			{
				foreach (var prop in c.Properties)
				{
					var name = p.UnionIsArgument ? prop.CliName : prop.FlagModeName;
					if (p.UnionIsArgument && ownPropTypes.TryGetValue(name, out var seen))
					{
						if (seen.Special != prop.Special || seen.TypeName != prop.TypeName)
							acc.Add(UnionFlagCollision, location, p.SymbolName, $"--{name}");
						continue;
					}

					if (claimed.ContainsKey(name))
						acc.Add(UnionFlagCollision, location, p.SymbolName, $"--{name}");
					else
						claimed[name] = p.SymbolName;
					if (UnionBoolDefaultsTrue(prop))
					{
						if (claimed.ContainsKey("no-" + name))
							acc.Add(UnionFlagCollision, location, p.SymbolName, $"--no-{name}");
						else
							claimed["no-" + name] = p.SymbolName;
					}
					if (p.UnionIsArgument)
						ownPropTypes[name] = (prop.Special, prop.TypeName);
				}
			}
		}
	}
}
