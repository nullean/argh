using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Nullean.Argh;

public sealed partial class CliParserGenerator
{
	private static void EmitOptionsTryParse(
		StringBuilder sb,
		string methodName,
		ImmutableArray<ParameterModel> members,
		string? storeTypeFq = null,
		string? storeFieldName = null,
		ImmutableArray<string>? storeBestCtorParamOrder = null,
		string? entryAssemblyName = null,
		ImmutableArray<ParameterModel>? deferLeadingRootAliasFlags = null)
	{
		var defer = deferLeadingRootAliasFlags ?? ImmutableArray<ParameterModel>.Empty;

		var syn = SyntheticOptionsCommand(members, methodName);
		var flagMembers = members.Where(static p => p.Kind == ParameterKind.Flag).ToList();
		var widthCandidates = new List<int> { "-h, --help".Length };
		widthCandidates.AddRange(flagMembers.Select(static p => HelpLayout.FormatOptionLeftCell(p).Length));
		var maxOptWidth = flagMembers.Count == 0
			? "-h, --help".Length
			: Math.Max(Math.Min(widthCandidates.Max(), 40), "-h, --help".Length);

		if (flagMembers.Count > 0)
			EmitOptionsTryParseFlagHelpPrinter(sb, methodName, flagMembers, maxOptWidth);

		var flagHelpMethodName = methodName + "_FlagHelp_ToStdErr";
		var emitRunHint = !string.IsNullOrEmpty(entryAssemblyName);
		var runHintFailUnknown = emitRunHint
			? $"\t\t\t\t\tConsole.Error.WriteLine(\"Run '{Escape(entryAssemblyName!)} --help' for usage.\");"
			: null;
		var runHintMissingLong = emitRunHint
			? $"\t\t\t\t\t\t\tConsole.Error.WriteLine(\"Run '{Escape(entryAssemblyName!)} --help' for usage.\");"
			: null;

		sb.AppendLine($"\t\tprivate static bool {methodName}(string[] args, int[] idx)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tvar flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);");
		EmitBoolSwitchNames(sb, syn);
		EmitCanonFlagNameMethod(sb, syn);
		EmitShortFlagMethods(sb, syn, multiFlagsAvailable: false);
		EmitAllowedFlagPredicate(sb, members);
		EmitDeferLeadingRootAliasHelpers(sb, defer);

		if (flagMembers.Count > 0)
		{
			sb.Append("\t\t\tvar __flagFuzzyCands = new string[] { ");
			var sortedNames = flagMembers
				.Select(static p => p.CliLongName)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
				.ToList();
			for (var i = 0; i < sortedNames.Count; i++)
			{
				if (i > 0)
					sb.Append(", ");
				sb.Append('"').Append(Escape(sortedNames[i])).Append('"');
			}

			sb.AppendLine(" };");
			sb.AppendLine("\t\t\tbool FailUnknownLongOption(string flagName)");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tvar __matches = FuzzyMatch.FindClosest(flagName, __flagFuzzyCands, {FuzzyMaxDistance});");
			sb.AppendLine("\t\t\t\tif (__matches.Count == 0)");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'.\");");
			if (runHintFailUnknown is not null)
			{
				sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
				sb.AppendLine(runHintFailUnknown);
			}

			sb.AppendLine("\t\t\t\t\treturn false;");
			sb.AppendLine("\t\t\t\t}");
			sb.AppendLine("\t\t\t\tif (__matches.Count == 1)");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\tvar __m = __matches[0];");
			sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'. Did you mean '--{__m}'?\");");
			sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
			sb.AppendLine($"\t\t\t\t\t{flagHelpMethodName}(__m);");
			if (runHintFailUnknown is not null)
			{
				sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
				sb.AppendLine(runHintFailUnknown);
			}

			sb.AppendLine("\t\t\t\t\treturn false;");
			sb.AppendLine("\t\t\t\t}");
			sb.AppendLine("\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'. Did you mean one of these?\");");
			sb.AppendLine("\t\t\t\tConsole.Error.WriteLine();");
			sb.AppendLine("\t\t\t\tforeach (var __m in __matches)");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine($"\t\t\t\t\t{flagHelpMethodName}(__m);");
			sb.AppendLine("\t\t\t\t}");
			if (runHintFailUnknown is not null)
			{
				sb.AppendLine("\t\t\t\tConsole.Error.WriteLine();");
				sb.AppendLine(runHintFailUnknown);
			}
			sb.AppendLine("\t\t\t\treturn false;");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine();
		}

		sb.AppendLine("\t\t\twhile (idx[0] < args.Length && args[idx[0]].Length > 0 && args[idx[0]][0] == '-')");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tif (args[idx[0]] == \"--help\" || args[idx[0]] == \"-h\" || args[idx[0]] == \"--version\")");
		sb.AppendLine("\t\t\t\t\tbreak;");
		sb.AppendLine("\t\t\t\tvar a = args[idx[0]];");
		sb.AppendLine("\t\t\t\tif (a.StartsWith(\"--\", StringComparison.Ordinal))");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\tvar eq = a.IndexOf('=');");
		sb.AppendLine("\t\t\t\t\tif (eq >= 0)");
		sb.AppendLine("\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\tvar flagName = CanonFlagName(a.Substring(2, eq - 2));");
		sb.AppendLine("\t\t\t\t\t\tif (!IsAllowedFlag(flagName))");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tif (ShouldDeferLeadingRootAliasCanon(flagName))");
		sb.AppendLine("\t\t\t\t\t\t\t\tbreak;");
		if (flagMembers.Count > 0)
			sb.AppendLine("\t\t\t\t\t\t\t\treturn FailUnknownLongOption(flagName);");
		else
		{
			sb.AppendLine("\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'.\");");
			sb.AppendLine("\t\t\t\t\t\t\treturn false;");
		}

		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\tvar flagValue = a.Substring(eq + 1);");
		sb.AppendLine("\t\t\t\t\t\tflags[flagName] = flagValue;");
		sb.AppendLine("\t\t\t\t\t\tidx[0]++;");
		sb.AppendLine("\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\telse");
		sb.AppendLine("\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\tvar flagName = CanonFlagName(a.Substring(2));");
		sb.AppendLine("\t\t\t\t\t\tif (!IsAllowedFlag(flagName))");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tif (ShouldDeferLeadingRootAliasCanon(flagName))");
		sb.AppendLine("\t\t\t\t\t\t\t\tbreak;");
		if (flagMembers.Count > 0)
			sb.AppendLine("\t\t\t\t\t\t\t\treturn FailUnknownLongOption(flagName);");
		else
		{
			sb.AppendLine("\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'.\");");
			sb.AppendLine("\t\t\t\t\t\t\treturn false;");
		}

		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\tif (IsBoolSwitchName(flagName))");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tflags[flagName] = IsBoolSwitchNoName(flagName) ? null : \"true\";");
		sb.AppendLine("\t\t\t\t\t\t\tidx[0]++;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\telse");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tif (idx[0] + 1 >= args.Length)");
		sb.AppendLine("\t\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: missing value for flag --{flagName}.\");");
		if (flagMembers.Count > 0)
		{
			sb.AppendLine("\t\t\t\t\t\t\t\tConsole.Error.WriteLine();");
			sb.AppendLine($"\t\t\t\t\t\t\t\t{flagHelpMethodName}(flagName);");
			if (runHintMissingLong is not null)
			{
				sb.AppendLine("\t\t\t\t\t\t\t\tConsole.Error.WriteLine();");
				sb.AppendLine(runHintMissingLong);
			}
		}

		sb.AppendLine("\t\t\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\t\tflags[flagName] = args[idx[0] + 1];");
		sb.AppendLine("\t\t\t\t\t\t\tidx[0] += 2;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\tcontinue;");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\tif (a.Length >= 2 && a[0] == '-' && a[1] != '-')");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\tvar eqs = a.IndexOf('=');");
		sb.AppendLine("\t\t\t\t\tif (eqs >= 0)");
		sb.AppendLine("\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\tvar shortKey = a.Substring(1, eqs - 1);");
		sb.AppendLine("\t\t\t\t\t\tif (shortKey.Length != 1)");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tConsole.Error.WriteLine(\"Error: short options must be a single letter (e.g. -e=value).\");");
		sb.AppendLine("\t\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\tif (!TryApplyShortFlag(shortKey[0], a.Substring(eqs + 1)))");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tif (ShouldDeferLeadingShortFlag(shortKey[0])) break;");
		sb.AppendLine("\t\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\tidx[0]++;");
		sb.AppendLine("\t\t\t\t\t\tcontinue;");
		sb.AppendLine("\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\tif (a.Length == 2)");
		sb.AppendLine("\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\tvar sc = a[1];");
		sb.AppendLine("\t\t\t\t\t\tif (IsShortBoolChar(sc))");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tif (!TryApplyShortFlag(sc, \"true\"))");
		sb.AppendLine("\t\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\t\tif (ShouldDeferLeadingShortFlag(sc)) break;");
		sb.AppendLine("\t\t\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\t\tidx[0]++;");
		sb.AppendLine("\t\t\t\t\t\t\tcontinue;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\tif (idx[0] + 1 >= args.Length)");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: missing value for short flag '-{sc}'.\");");
		sb.AppendLine("\t\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\tif (!TryApplyShortFlag(sc, args[idx[0] + 1]))");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tif (ShouldDeferLeadingShortFlag(sc)) break;");
		sb.AppendLine("\t\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\tidx[0] += 2;");
		sb.AppendLine("\t\t\t\t\t\tcontinue;");
		sb.AppendLine("\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine(\"Error: combined short flags (e.g. -abc) are not supported.\");");
		sb.AppendLine("\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\tConsole.Error.WriteLine($\"Error: unexpected token '{a}'.\");");
		sb.AppendLine("\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t}");
		if (storeTypeFq is not null && storeFieldName is not null && members.Length > 0)
			EmitOptionsConstructAndStore(sb, storeTypeFq, members, storeFieldName, storeBestCtorParamOrder);
		sb.AppendLine("\t\t\treturn true;");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	/// <summary>
	/// After <see cref="EmitOptionsTryParse"/> parses flags into a <c>flags</c> dict, extract member values and
	/// construct the options instance, then store it in <paramref name="storeFieldName"/>.
	/// Injected just before <c>return true</c> of the parse method.
	/// </summary>
	private static void EmitOptionsConstructAndStore(
		StringBuilder sb,
		string typeFq,
		ImmutableArray<ParameterModel> members,
		string storeFieldName,
		ImmutableArray<string>? bestCtorParamOrder)
	{
		var byName = members.ToDictionary(static m => m.SymbolName, StringComparer.OrdinalIgnoreCase);

		// For cross-assembly options types the property initializer syntax is not readable.
		// Instantiate the type once to capture all C# runtime defaults.
		var hasRtDefaults = members.Any(static m => m.UsesRuntimeDefault);
		if (hasRtDefaults)
			sb.AppendLine($"\t\t\tvar __rt_default = new {typeFq}();");

		// Extract each member's value from the flags dict.
		foreach (var m in members)
		{
			if (m.Kind != ParameterKind.Flag)
				continue;
			if (m.Special == BoolSpecialKind.Bool)
				sb.AppendLine($"\t\t\tvar {m.LocalVarName} = flags.ContainsKey(\"{Escape(m.CliLongName)}\");");
			else if (m.Special == BoolSpecialKind.NullableBool)
			{
				sb.AppendLine($"\t\t\tbool? {m.LocalVarName} = null;");
				sb.AppendLine($"\t\t\tif (flags.TryGetValue(\"{Escape(m.CliLongName)}\", out var {m.LocalVarName}_yv))");
				sb.AppendLine($"\t\t\t\t{m.LocalVarName} = ParseNullableBool({m.LocalVarName}_yv, true);");
				sb.AppendLine($"\t\t\tif (flags.TryGetValue(\"no-{Escape(m.CliLongName)}\", out var {m.LocalVarName}_nv))");
				sb.AppendLine($"\t\t\t\t{m.LocalVarName} = ParseNullableBool({m.LocalVarName}_nv, false);");
			}
			else
			{
				// Declare the local variable first (EmitParseAndAssign only assigns, does not declare).
				// For cross-assembly runtime-default properties, seed from the pre-created instance.
				var initializer = m.UsesRuntimeDefault && hasRtDefaults
					? $"__rt_default.{m.SymbolName}"
					: GetCliInitializer(m);
				sb.AppendLine($"\t\t\t{GetCSharpCliType(m)} {m.LocalVarName} = {initializer};");
				var canOmitFlag = !m.IsRequired || m.DefaultValueLiteral is not null;
				if (canOmitFlag)
				{
					sb.AppendLine($"\t\t\tif (flags.TryGetValue(\"{Escape(m.CliLongName)}\", out var {m.LocalVarName}Text) && {m.LocalVarName}Text is not null)");
					sb.AppendLine("\t\t\t{");
					EmitParseAndAssign(sb, m, m.LocalVarName + "Text", m.LocalVarName, "return false", null);
					sb.AppendLine("\t\t\t}");
				}
				else
				{
					sb.AppendLine($"\t\t\tif (!flags.TryGetValue(\"{Escape(m.CliLongName)}\", out var {m.LocalVarName}Text) || {m.LocalVarName}Text is null)");
					sb.AppendLine("\t\t\t{");
					sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine($\"Error: missing required flag --{Escape(m.CliLongName)}.\");");
					sb.AppendLine("\t\t\t\treturn false;");
					sb.AppendLine("\t\t\t}");
					EmitParseAndAssign(sb, m, m.LocalVarName + "Text", m.LocalVarName, "return false", null);
				}
			}
		}

		// Construct using the primary constructor if all members align, otherwise property assignment.
		if (bestCtorParamOrder is { } ctorOrder && ctorOrder.Length == members.Length)
		{
			sb.Append($"\t\t\t{storeFieldName} = new {typeFq}(");
			for (var i = 0; i < ctorOrder.Length; i++)
			{
				if (i > 0) sb.Append(", ");
				sb.Append(byName[ctorOrder[i]].LocalVarName);
			}

			sb.AppendLine(");");
		}
		else
		{
			sb.AppendLine($"\t\t\t{storeFieldName} = new {typeFq}();");
			foreach (var m in members)
				sb.AppendLine($"\t\t\t{storeFieldName}.{m.SymbolName} = {m.LocalVarName};");
		}
	}

	private static void EmitIsMultiFlagPredicate(StringBuilder sb, CommandModel cmd)
	{
		var names = new List<string>();
		foreach (var p in cmd.Parameters)
		{
			if (p is { IsCollection: true, Kind: ParameterKind.Flag } && p.CollectionSeparator is null)
				names.Add(p.CliLongName);
		}

		if (names.Count == 0)
		{
			sb.AppendLine("\t\t\tbool IsMultiFlag(string name) => false;");
			return;
		}

		sb.AppendLine("\t\t\tbool IsMultiFlag(string name) => name switch");
		sb.AppendLine("\t\t\t{");
		foreach (var n in names)
			sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");
	}

	/// <summary>
	/// Emits local variable reconstruction for each options type in the injection chain.
	/// For every member: prefer the value from the command's <c>flags</c> dict (post-command flags),
	/// fall back to the pre-parsed static field (pre-command flags). This ensures flags work in
	/// either position: <c>myapp --verbose cmd</c> or <c>myapp cmd --verbose</c>.
	/// </summary>
	private static void EmitOptionsReconstructLocals(
		StringBuilder sb,
		ImmutableArray<(string TypeFq, string TypeMetadataName, ImmutableArray<string> AllBaseTypeMetadataNames, string StaticFieldName, string LocalVarName, ImmutableArray<ParameterModel> FlatMembers, ImmutableArray<string>? BestCtorParamOrder)> chain)
	{
		if (chain.IsDefaultOrEmpty) return;

		// Track which static provides the fallback for each CLI name (first in chain that declares it).
		// Key = CliLongName, Value = "{staticFieldName}.{SymbolName}"
		var fallbackMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (_, _, _, staticField, _, flatMembers, _) in chain)
		{
			foreach (var m in flatMembers)
			{
				if (!fallbackMap.ContainsKey(m.CliLongName))
					fallbackMap[m.CliLongName] = staticField + "." + m.SymbolName;
			}
		}

		// Track which member vars have already been emitted (across chain entries, to avoid re-declaration).
		var emittedTmpVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var (typeFq, _, _, _, localVar, flatMembers, bestCtorParamOrder) in chain)
		{
			if (flatMembers.IsEmpty) continue;
			var byName = flatMembers.ToDictionary(static m => m.SymbolName, StringComparer.OrdinalIgnoreCase);

			// Extract each member: command-level flags take precedence over pre-parsed static value.
			foreach (var m in flatMembers)
			{
				var fallback = fallbackMap.TryGetValue(m.CliLongName, out var fb) ? fb : "default";
				var tmpName = "__ropt_" + m.LocalVarName;
				// Skip if already emitted by a parent type in the chain (inherited members appear in multiple flat lists).
				if (!emittedTmpVars.Add(tmpName)) continue;
				if (m.Special == BoolSpecialKind.Bool)
				{
					sb.AppendLine($"\t\t\tvar {tmpName} = flags.ContainsKey(\"{Escape(m.CliLongName)}\") || {fallback};");
				}
				else if (m.Special == BoolSpecialKind.NullableBool)
				{
					sb.AppendLine($"\t\t\tbool? {tmpName} = {fallback};");
					sb.AppendLine($"\t\t\tif (flags.TryGetValue(\"{Escape(m.CliLongName)}\", out var {tmpName}_yv))");
					sb.AppendLine($"\t\t\t\t{tmpName} = ParseNullableBool({tmpName}_yv, true);");
					sb.AppendLine($"\t\t\tif (flags.TryGetValue(\"no-{Escape(m.CliLongName)}\", out var {tmpName}_nv))");
					sb.AppendLine($"\t\t\t\t{tmpName} = ParseNullableBool({tmpName}_nv, false);");
				}
				else
				{
					// For value-typed flags: if found in command flags use that; else keep static fallback value.
					sb.AppendLine($"\t\t\tflags.TryGetValue(\"{Escape(m.CliLongName)}\", out var {tmpName}Txt);");
					sb.AppendLine($"\t\t\tvar {tmpName} = {fallback};");
					if (m.ScalarKind == CliScalarKind.Primitive)
					{
						// Re-parse from text if present, keeping static value if not.
						var parseExpr = m.TypeName switch
						{
							"int" => $"int.TryParse({tmpName}Txt, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"int?" =>
								$"int.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? (int?){tmpName}P : {tmpName}",
							"long" => $"long.TryParse({tmpName}Txt, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"long?" =>
								$"long.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? (long?){tmpName}P : {tmpName}",
							"double" =>
								$"double.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"double?" =>
								$"double.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? (double?){tmpName}P : {tmpName}",
							"float" =>
								$"float.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"float?" =>
								$"float.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? (float?){tmpName}P : {tmpName}",
							"decimal" =>
								$"decimal.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"decimal?" =>
								$"decimal.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? (decimal?){tmpName}P : {tmpName}",
							"DateTime" =>
								$"global::System.DateTime.TryParse({tmpName}Txt, System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind | global::System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"DateTime?" =>
								$"global::System.DateTime.TryParse({tmpName}Txt, System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind | global::System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var {tmpName}P) ? (global::System.DateTime?){tmpName}P : {tmpName}",
							"DateTimeOffset" =>
								$"global::System.DateTimeOffset.TryParse({tmpName}Txt, System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind | global::System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"DateTimeOffset?" =>
								$"global::System.DateTimeOffset.TryParse({tmpName}Txt, System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind | global::System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var {tmpName}P) ? (global::System.DateTimeOffset?){tmpName}P : {tmpName}",
							"TimeSpan" =>
								$"global::Nullean.Argh.ArghTimeSpan.TryParse({tmpName}Txt, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"TimeSpan?" =>
								$"global::Nullean.Argh.ArghTimeSpan.TryParse({tmpName}Txt, out var {tmpName}P) ? (global::System.TimeSpan?){tmpName}P : {tmpName}",
							"DateOnly" =>
								$"global::System.DateOnly.TryParse({tmpName}Txt, System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.None, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"DateOnly?" =>
								$"global::System.DateOnly.TryParse({tmpName}Txt, System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.None, out var {tmpName}P) ? (global::System.DateOnly?){tmpName}P : {tmpName}",
							"string" or "string?" => $"{tmpName}Txt ?? {tmpName}",
							_ => $"{tmpName}Txt != null ? {tmpName}Txt : {tmpName}"
						};
						if (m.TypeName is "string" or "string?")
							sb.AppendLine($"\t\t\t{tmpName} = {parseExpr};");
						else
							sb.AppendLine($"\t\t\tif ({tmpName}Txt != null) {tmpName} = {parseExpr};");
					}
					else if (m.ScalarKind == CliScalarKind.Enum && m.EnumTypeFq is not null)
					{
						// Re-parse enum from command-trailing flags; null-guard required (TryGetValue out-var is string?).
						// On invalid value: keep static fallback silently (no user-visible error; leading globals were already validated).
						var evVar = "__ev_ropt_" + m.LocalVarName;
						sb.AppendLine($"\t\t\tif ({tmpName}Txt is not null)");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tif (global::System.Enum.TryParse<{m.EnumTypeFq}>({tmpName}Txt, true, out var {evVar}) && global::System.Enum.IsDefined(typeof({m.EnumTypeFq}), {evVar}))");
						sb.AppendLine($"\t\t\t\t\t{tmpName} = {evVar};");
						sb.AppendLine("\t\t\t}");
					}
					else if (m.ScalarKind == CliScalarKind.FileInfo)
					{
						sb.AppendLine($"\t\t\tif ({tmpName}Txt is not null)");
						sb.AppendLine("\t\t\t{");
						if (m.ExpandUserProfileBeforeBind)
						{
							var expanded = "__path_ropt_" + m.LocalVarName;
							sb.AppendLine($"\t\t\t\tvar {expanded} = global::Nullean.Argh.ArghPath.ExpandUserProfilePath({tmpName}Txt);");
							sb.AppendLine($"\t\t\t\t{tmpName} = new global::System.IO.FileInfo({expanded});");
						}
						else
							sb.AppendLine($"\t\t\t\t{tmpName} = new global::System.IO.FileInfo({tmpName}Txt);");
						sb.AppendLine("\t\t\t}");
					}
					else if (m.ScalarKind == CliScalarKind.DirectoryInfo)
					{
						sb.AppendLine($"\t\t\tif ({tmpName}Txt is not null)");
						sb.AppendLine("\t\t\t{");
						if (m.ExpandUserProfileBeforeBind)
						{
							var expanded = "__path_ropt_" + m.LocalVarName;
							sb.AppendLine($"\t\t\t\tvar {expanded} = global::Nullean.Argh.ArghPath.ExpandUserProfilePath({tmpName}Txt);");
							sb.AppendLine($"\t\t\t\t{tmpName} = new global::System.IO.DirectoryInfo({expanded});");
						}
						else
							sb.AppendLine($"\t\t\t\t{tmpName} = new global::System.IO.DirectoryInfo({tmpName}Txt);");
						sb.AppendLine("\t\t\t}");
					}
					else if (m.ScalarKind == CliScalarKind.Uri)
					{
						var uriVar = "__uri_ropt_" + m.LocalVarName;
						sb.AppendLine($"\t\t\tif ({tmpName}Txt is not null && global::System.Uri.TryCreate({tmpName}Txt, global::System.UriKind.RelativeOrAbsolute, out var {uriVar}))");
						sb.AppendLine($"\t\t\t\t{tmpName} = {uriVar};");
					}
					else if (m.ScalarKind == CliScalarKind.CustomParser && m.ParserTypeFq is not null)
					{
						var parserVar = "__parser_ropt_" + m.LocalVarName;
						var pvVar = "__pv_ropt_" + m.LocalVarName;
						sb.AppendLine($"\t\t\tif ({tmpName}Txt is not null)");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tvar {parserVar} = new {m.ParserTypeFq}();");
						sb.AppendLine($"\t\t\t\tif ({parserVar}.TryParse({tmpName}Txt, out var {pvVar}))");
						sb.AppendLine($"\t\t\t\t\t{tmpName} = {pvVar};");
						sb.AppendLine("\t\t\t}");
					}
				}
			}

			// Construct the local options instance using pre-computed constructor order.
			if (bestCtorParamOrder is { } ctorOrder && ctorOrder.Length == flatMembers.Length)
			{
				sb.Append($"\t\t\tvar {localVar} = new {typeFq}(");
				for (var i = 0; i < ctorOrder.Length; i++)
				{
					if (i > 0) sb.Append(", ");
					sb.Append("__ropt_" + byName[ctorOrder[i]].LocalVarName);
				}
				sb.AppendLine(");");
			}
			else
			{
				sb.AppendLine($"\t\t\tvar {localVar} = new {typeFq}();");
				foreach (var m in flatMembers)
					sb.AppendLine($"\t\t\t{localVar}.{m.SymbolName} = __ropt_{m.LocalVarName};");
			}
		}
	}

	private static void EmitBindCollectionParameter(StringBuilder sb, ParameterModel p, bool multiFlagsAvailable, string failureExit = "return 2", string? helpMethodName = null,
		string? flagHelpStdErrMethodName = null, string? parseFailureRunHint = null)
	{
		var flagKey = Escape(p.CliLongName);
		var acc = p.LocalVarName + "_acc";
		var elemModel = ForElementParsing(p);
		if (p.CollectionSeparator is string sep)
		{
			sb.AppendLine($"\t\t\tif (!flags.TryGetValue(\"{flagKey}\", out var {p.LocalVarName}Joined))");
			if (p.IsRequired)
			{
				sb.AppendLine("\t\t\t{");
				sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine($\"Error: missing required flag --{flagKey}.\");");
				EmitAfterCliParseErrorHelp(sb, p, "\t\t\t\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
				sb.AppendLine($"\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t}");
			}
			else
			{
				sb.AppendLine($"\t\t\t\t{p.LocalVarName}Joined = null;");
			}

			sb.AppendLine($"\t\t\tif (!string.IsNullOrEmpty({p.LocalVarName}Joined))");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tvar __sep_{p.LocalVarName} = \"{Escape(sep)}\";");
			sb.AppendLine($"\t\t\t\tforeach (var __part in {p.LocalVarName}Joined.Split(__sep_{p.LocalVarName}, StringSplitOptions.None))");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\tif (string.IsNullOrEmpty(__part)) continue;");
			EmitParseFromString(sb, elemModel, "__part", "__ce_" + p.LocalVarName, indentExtra: "\t\t", outVarKeyword: true, failureExit: failureExit, helpMethodName: helpMethodName, flagHelpStdErrMethodName: flagHelpStdErrMethodName, parseFailureRunHint: parseFailureRunHint);
			if (p.CollectionTargetIsReadOnlySet)
			{
				sb.AppendLine($"\t\t\t\t\tif (!{acc}.Add(__ce_{p.LocalVarName}))");
				sb.AppendLine("\t\t\t\t\t{");
				sb.AppendLine($"\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: duplicate value '{{__ce_{p.LocalVarName}}}' for --{flagKey}.\");");
				sb.AppendLine($"\t\t\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t\t\t}");
			}
			else
			{
				sb.AppendLine($"\t\t\t\t\t{acc}.Add(__ce_{p.LocalVarName});");
			}
			sb.AppendLine("\t\t\t\t}");
			sb.AppendLine("\t\t\t}");
		}
		else
		{
			if (!multiFlagsAvailable)
				return;

			sb.AppendLine($"\t\t\tif (!multiFlags.TryGetValue(\"{flagKey}\", out var __rawList_{p.LocalVarName}))");
			sb.AppendLine($"\t\t\t\t__rawList_{p.LocalVarName} = new List<string>();");
			sb.AppendLine($"\t\t\tforeach (var __raw in __rawList_{p.LocalVarName})");
			sb.AppendLine("\t\t\t{");
			EmitParseFromString(sb, elemModel, "__raw", "__ce_" + p.LocalVarName, indentExtra: "\t", outVarKeyword: true, failureExit: failureExit, helpMethodName: helpMethodName, flagHelpStdErrMethodName: flagHelpStdErrMethodName, parseFailureRunHint: parseFailureRunHint);
			if (p.CollectionTargetIsReadOnlySet)
			{
				sb.AppendLine($"\t\t\t\tif (!{acc}.Add(__ce_{p.LocalVarName}))");
				sb.AppendLine("\t\t\t\t{");
				sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Error: duplicate value '{{__ce_{p.LocalVarName}}}' for --{flagKey}.\");");
				sb.AppendLine($"\t\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t\t}");
			}
			else
			{
				sb.AppendLine($"\t\t\t\t{acc}.Add(__ce_{p.LocalVarName});");
			}
			sb.AppendLine("\t\t\t}");
			if (p.IsRequired)
			{
				sb.AppendLine($"\t\t\tif ({acc}.Count == 0)");
				sb.AppendLine("\t\t\t{");
				sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine($\"Error: missing required flag --{flagKey}.\");");
				EmitAfterCliParseErrorHelp(sb, p, "\t\t\t\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
				sb.AppendLine($"\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t}");
			}
		}

		var declType = p.FullDeclaredTypeFq ?? "object";
		var useNullWhenUnset = !p.IsRequired && p.DeclaredNullableAnnotated;
		if (useNullWhenUnset)
		{
			if (p.CollectionTargetIsArray)
				sb.AppendLine($"\t\t\t{declType} {p.LocalVarName} = {acc}.Count == 0 ? null : {acc}.ToArray();");
			else
				sb.AppendLine($"\t\t\t{declType} {p.LocalVarName} = {acc}.Count == 0 ? null : {acc};");
		}
		else if (p.CollectionTargetIsArray)
			sb.AppendLine($"\t\t\t{declType} {p.LocalVarName} = {acc}.ToArray();");
		else
			sb.AppendLine($"\t\t\t{declType} {p.LocalVarName} = {acc};");
	}

	private static void EmitAsParametersConstruction(StringBuilder sb, CommandModel cmd)
	{
		if (cmd.HandlerParamTypes.IsDefaultOrEmpty)
			return;

		foreach (var mp in cmd.HandlerParamTypes)
		{
			if (!mp.IsAsParameters)
				continue;

			var group = cmd.Parameters
				.Where(p => p.AsParametersOwnerParamName == mp.Name)
				.OrderBy(p => p.AsParametersMemberOrder)
				.ToArray();
			if (group.Length == 0)
				continue;

			var typeFq = group[0].AsParametersTypeFq;
			if (typeFq is null)
				continue;

			var varName = AsParametersConstructedVarName(mp.Name);
			var ctor = group.Where(p => !p.AsParametersUseInit).ToArray();
			var init = group.Where(p => p.AsParametersUseInit).ToArray();
			sb.Append($"\t\t\tvar {varName} = new {typeFq}(");
			for (var i = 0; i < ctor.Length; i++)
			{
				if (i > 0)
					sb.Append(", ");
				sb.Append(ctor[i].Kind == ParameterKind.Injected ? "ct" : ctor[i].LocalVarName);
			}

			sb.Append(")");
			if (init.Length > 0)
			{
				sb.AppendLine();
				sb.AppendLine("\t\t\t{");
				foreach (var ip in init)
				{
					var rhs = ip.Kind == ParameterKind.Injected ? "ct" : ip.LocalVarName;
					sb.AppendLine($"\t\t\t\t{ip.AsParametersClrName} = {rhs},");
				}

				sb.AppendLine("\t\t\t};");
			}
			else
			{
				sb.AppendLine(";");
			}
		}
	}

	private static void EmitAsParametersConstructionForDto(StringBuilder sb, CommandModel cmd)
	{
		var group = cmd.Parameters
			.Where(static p => p.AsParametersOwnerParamName is not null)
			.OrderBy(static p => p.AsParametersMemberOrder)
			.ToArray();
		if (group.Length == 0)
		{
			sb.AppendLine("\t\t\treturn false;");
			return;
		}

		var typeFq = group[0].AsParametersTypeFq;
		if (typeFq is null)
		{
			sb.AppendLine("\t\t\treturn false;");
			return;
		}

		var ctor = group.Where(static p => !p.AsParametersUseInit).ToArray();
		var init = group.Where(static p => p.AsParametersUseInit).ToArray();
		sb.Append("\t\t\tvar __dto = new ").Append(typeFq).Append("(");
		for (var i = 0; i < ctor.Length; i++)
		{
			if (i > 0)
				sb.Append(", ");
			sb.Append(ctor[i].Kind == ParameterKind.Injected
				? "default(global::System.Threading.CancellationToken)"
				: ctor[i].LocalVarName);
		}

		sb.Append(")");
		if (init.Length > 0)
		{
			sb.AppendLine();
			sb.AppendLine("\t\t\t{");
			foreach (var ip in init)
			{
				var rhs = ip.Kind == ParameterKind.Injected
					? "default(global::System.Threading.CancellationToken)"
					: ip.LocalVarName;
				sb.AppendLine($"\t\t\t\t{ip.AsParametersClrName} = {rhs},");
			}

			sb.AppendLine("\t\t\t};");
		}
		else
		{
			sb.AppendLine(";");
		}

		sb.AppendLine("\t\t\tvalue = __dto;");
		sb.AppendLine("\t\t\treturn true;");
	}

	private static void EmitOptionsDtoConstructionAndReturn(StringBuilder sb, string typeFq, ImmutableArray<ParameterModel> members, ImmutableArray<string>? bestCtorParamOrder)
	{
		var byName = members.ToDictionary(static m => m.SymbolName, StringComparer.OrdinalIgnoreCase);

		if (bestCtorParamOrder is { } ctorOrder && ctorOrder.Length > 0 && ctorOrder.Length == members.Length)
		{
			sb.Append("\t\t\tvalue = new ").Append(typeFq).Append("(");
			for (var i = 0; i < ctorOrder.Length; i++)
			{
				if (i > 0)
					sb.Append(", ");
				sb.Append(byName[ctorOrder[i]].LocalVarName);
			}

			sb.AppendLine(");");
			sb.AppendLine("\t\t\treturn true;");
			return;
		}

		sb.AppendLine($"\t\t\tvar __dto = new {typeFq}();");
		foreach (var m in members)
			sb.AppendLine($"\t\t\t__dto.{m.SymbolName} = {m.LocalVarName};");

		sb.AppendLine("\t\t\tvalue = __dto;");
		sb.AppendLine("\t\t\treturn true;");
	}

	private static string AsParametersConstructedVarName(string methodParameterName) =>
		"__as_" + Naming.SanitizeIdentifier(methodParameterName);

	private static void EmitValidationChecks(
		StringBuilder sb,
		CommandModel cmd,
		string failureExit,
		string? entryAssemblyName,
		string? flagHelpStdErrMethodName = null)
	{
		foreach (var p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Injected || p.Kind == ParameterKind.OptionsInjected)
				continue;
			if (p.Validations.IsDefaultOrEmpty)
				continue;

			var cliName = p.CliLongName;
			var varName = p.LocalVarName;
			var isNullable = !p.IsRequired;
			var isNullableValueType = isNullable && p.Special == BoolSpecialKind.None
				&& p.ScalarKind == CliScalarKind.Primitive && p.TypeName != "string"
				&& p.TypeName.EndsWith("?", StringComparison.Ordinal);

			// Build the run-hint line (baked in as a string literal)
			string? runHint = null;
			if (entryAssemblyName is not null && !string.IsNullOrEmpty(cmd.CommandName))
			{
				var route = cmd.RoutePrefix.IsDefaultOrEmpty
					? ""
					: string.Join(" ", cmd.RoutePrefix) + " ";
				runHint = $"Run '{Escape(entryAssemblyName)} {Escape(route)}{Escape(cmd.CommandName)} --help' for usage.";
			}

			if (p.IsCollection)
				EmitCollectionFilesystemValidation(sb, p, cliName, varName, failureExit, flagHelpStdErrMethodName, runHint);

			foreach (var constraint in p.Validations)
			{
				// Filesystem-path-family constraints on collections are handled per-element above
				// (varName is the whole list/array, not a single FileInfo/DirectoryInfo instance).
				if (p.IsCollection && IsCollectionFilesystemConstraint(constraint))
					continue;

				switch (constraint)
				{
					case RangeConstraint r:
					{
						var guard = isNullableValueType ? $"{varName}.HasValue && (" : "";
						var closeGuard = isNullableValueType ? ")" : "";
						var access = isNullableValueType ? $"{varName}.Value" : varName;
						sb.AppendLine($"\t\t\tif ({guard}{access} < {r.MinLiteral} || {access} > {r.MaxLiteral}{closeGuard})");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: value must be between {Escape(r.MinLiteral.Trim('"'))} and {Escape(r.MaxLiteral.Trim('"'))}.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case TimeSpanRangeConstraint tsr:
					{
						var tsMin = "__tsRangeMin_" + varName;
						var tsMax = "__tsRangeMax_" + varName;
						sb.AppendLine($"\t\t\tif (!global::Nullean.Argh.ArghTimeSpan.TryParse({tsr.MinLiteral}, out var {tsMin}) || !global::Nullean.Argh.ArghTimeSpan.TryParse({tsr.MaxLiteral}, out var {tsMax}))");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: invalid TimeSpanRange bounds.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						var guard = isNullableValueType ? $"{varName}.HasValue && (" : "";
						var closeGuard = isNullableValueType ? ")" : "";
						var access = isNullableValueType ? $"{varName}.Value" : varName;
						sb.AppendLine($"\t\t\tif ({guard}{access} < {tsMin} || {access} > {tsMax}{closeGuard})");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: value must be between {Escape(tsr.MinLiteral.Trim('"'))} and {Escape(tsr.MaxLiteral.Trim('"'))}.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case CollectionCountConstraint cc:
					{
						var lenExpr = p.CollectionTargetIsArray ? $"{varName}.Length" : $"{varName}.Count";
						var nullGuard = !p.IsRequired && p.DeclaredNullableAnnotated ? $"{varName} != null && " : "";
						string ccCond;
						string ccMsg;
						if (cc.Min.HasValue && cc.Max.HasValue)
						{
							ccCond = $"{nullGuard}({lenExpr} < {cc.Min} || {lenExpr} > {cc.Max})";
							ccMsg = $"must have between {cc.Min} and {cc.Max} items.";
						}
						else if (cc.Min.HasValue)
						{
							ccCond = $"{nullGuard}{lenExpr} < {cc.Min}";
							ccMsg = $"must have at least {cc.Min} items.";
						}
						else
						{
							ccCond = $"{nullGuard}{lenExpr} > {cc.Max}";
							ccMsg = $"must have at most {cc.Max} items.";
						}
						var ccPrefix = p.Kind == ParameterKind.Positional ? $"<{Escape(cliName)}>" : $"--{Escape(cliName)}";
						sb.AppendLine($"\t\t\tif ({ccCond})");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: {ccPrefix}: {Escape(ccMsg)}\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case StringLengthConstraint s:
					{
						// For required non-nullable strings: access with ! to avoid introducing a null-path
						// in the condition (which would cause CS8604 at the handler call site).
						// For optional strings: wrap with a null guard.
						var sv = isNullable ? varName : varName + "!";
						var nullPrefix = isNullable ? $"{varName} != null && " : "";
						string cond;
						string msg;
						if (s.Min.HasValue && s.Max.HasValue)
						{
							cond = $"{nullPrefix}({sv}.Length < {s.Min} || {sv}.Length > {s.Max})";
							msg = $"value must be between {s.Min} and {s.Max} characters.";
						}
						else if (s.Min.HasValue)
						{
							cond = $"{nullPrefix}{sv}.Length < {s.Min}";
							msg = $"value must be at least {s.Min} characters.";
						}
						else
						{
							cond = $"{nullPrefix}{sv}.Length > {s.Max}";
							msg = $"value must be at most {s.Max} characters.";
						}
						sb.AppendLine($"\t\t\tif ({cond})");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: {Escape(msg)}\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case RegexConstraint rx:
					{
						var rv = isNullable ? varName : varName + "!";
						var cond = isNullable
							? $"{varName} != null && !global::System.Text.RegularExpressions.Regex.IsMatch({rv}, @\"{EscapeVerbatimString(rx.Pattern)}\")"
							: $"!global::System.Text.RegularExpressions.Regex.IsMatch({rv}, @\"{EscapeVerbatimString(rx.Pattern)}\")";
						sb.AppendLine($"\t\t\tif ({cond})");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: value does not match required pattern {Escape(rx.Pattern)}.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case AllowedValuesConstraint av:
					{
						var isStringType = p.TypeName == "string";
						string cond;
						if (isStringType)
						{
							var avv = isNullable ? varName : varName + "!";
							var checks = av.Values
								.Select(v => $"!string.Equals({avv}, {v}, global::System.StringComparison.Ordinal)")
								.ToList();
							var nullGuard = isNullable ? $"{varName} != null && " : "";
							cond = $"{nullGuard}({string.Join(" && ", checks)})";
						}
						else
						{
							var checks = av.Values.Select(v => $"{varName} != {v}").ToList();
							var nullGuard = isNullableValueType ? $"{varName}.HasValue && " : "";
							var access = isNullableValueType ? $"{varName}.Value" : varName;
							cond = $"{nullGuard}({string.Join(" && ", checks.Select(c => c.Replace(varName, access)))})";
						}
						var displayVals = string.Join(", ", av.Values.Select(v => v.Trim('"')));
						sb.AppendLine($"\t\t\tif ({cond})");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: value must be one of: {Escape(displayVals)}.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case DeniedValuesConstraint dv:
					{
						var isStringType = p.TypeName == "string";
						string cond;
						if (isStringType)
						{
							var dvv = isNullable ? varName : varName + "!";
							var checks = dv.Values
								.Select(v => $"string.Equals({dvv}, {v}, global::System.StringComparison.Ordinal)")
								.ToList();
							var nullGuard = isNullable ? $"{varName} != null && " : "";
							cond = $"{nullGuard}({string.Join(" || ", checks)})";
						}
						else
						{
							var checks = dv.Values.Select(v => $"{varName} == {v}").ToList();
							var nullGuard = isNullableValueType ? $"{varName}.HasValue && " : "";
							var access = isNullableValueType ? $"{varName}.Value" : varName;
							cond = $"{nullGuard}({string.Join(" || ", checks.Select(c => c.Replace(varName, access)))})";
						}
						var displayVals = string.Join(", ", dv.Values.Select(v => v.Trim('"')));
						sb.AppendLine($"\t\t\tif ({cond})");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: value must not be: {Escape(displayVals)}.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case EmailConstraint:
					{
						// Simple email check: at least one char, @, at least one char (DataAnnotations-compatible)
						var ev = isNullable ? varName : varName + "!";
						var cond = isNullable
							? $"{varName} != null && ({ev}.IndexOf('@') < 1 || {ev}.IndexOf('@') == {ev}.Length - 1)"
							: $"({ev}.IndexOf('@') < 1 || {ev}.IndexOf('@') == {ev}.Length - 1)";
						sb.AppendLine($"\t\t\tif ({cond})");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: value is not a valid email address.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case UrlConstraint:
					{
						// Validates absolute URL with http, https, or ftp scheme
						sb.AppendLine($"\t\t\tif ({(isNullable ? $"{varName} != null && " : "")}!");
						sb.AppendLine($"\t\t\t\t(global::System.Uri.TryCreate({varName}, global::System.UriKind.Absolute, out var __urlCheck_{varName}) &&");
						sb.AppendLine($"\t\t\t\t (__urlCheck_{varName}.Scheme == \"http\" || __urlCheck_{varName}.Scheme == \"https\" || __urlCheck_{varName}.Scheme == \"ftp\")))");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: value is not a valid URL.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case UriSchemeConstraint us:
					{
						// varName is a Uri? or Uri instance (already parsed)
						var access = isNullable ? $"{varName}!" : varName;
						var schemeChecks = us.Schemes
							.Select(s => $"{access}.Scheme == \"{Escape(s)}\"")
							.ToList();
						var nullGuard = isNullable ? $"{varName} != null && " : "";
						var displaySchemes = string.Join(", ", us.Schemes);
						sb.AppendLine($"\t\t\tif ({nullGuard}(!{access}.IsAbsoluteUri || !({string.Join(" || ", schemeChecks)})))");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: URI scheme must be one of: {Escape(displaySchemes)}.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case RejectSymbolicLinksConstraint:
					{
						var access = isNullable ? $"{varName}!" : varName;
						var nullGuard = isNullable ? $"{varName} != null && " : "";
						sb.AppendLine($"\t\t\tif ({nullGuard}global::Nullean.Argh.ArghIO.PathIsSymbolicOrReparsePoint({access}.FullName))");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: path must not be a symbolic link or reparse point.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
					case ExistingPathConstraint:
					{
						var access = isNullable ? $"{varName}!" : varName;
						var nullGuard = isNullable ? $"{varName} != null && " : "";
						if (p.ScalarKind == CliScalarKind.FileInfo)
						{
							sb.AppendLine($"\t\t\tif ({nullGuard}!global::System.IO.File.Exists({access}.FullName))");
							sb.AppendLine("\t\t\t{");
							sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: file does not exist.\");");
							EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
							sb.AppendLine($"\t\t\t\t{failureExit};");
							sb.AppendLine("\t\t\t}");
						}
						else
						{
							sb.AppendLine($"\t\t\tif ({nullGuard}!global::System.IO.Directory.Exists({access}.FullName))");
							sb.AppendLine("\t\t\t{");
							sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: directory does not exist.\");");
							EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
							sb.AppendLine($"\t\t\t\t{failureExit};");
							sb.AppendLine("\t\t\t}");
						}
						break;
					}
					case NonExistingPathConstraint:
					{
						var access = isNullable ? $"{varName}!" : varName;
						var nullGuard = isNullable ? $"{varName} != null && " : "";
						sb.AppendLine($"\t\t\tif ({nullGuard}(global::System.IO.File.Exists({access}.FullName) || global::System.IO.Directory.Exists({access}.FullName)))");
						sb.AppendLine("\t\t\t{");
						if (p.ScalarKind == CliScalarKind.FileInfo)
							sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: path already exists or is occupied by a directory.\");");
						else
							sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: path already exists.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}

					case FileExtensionsConstraint fe:
					{
						// varName is a FileInfo? or FileInfo instance
						var access = isNullable ? $"{varName}!" : varName;
						var extChecks = fe.Extensions
							.Select(ext => $"!string.Equals(global::System.IO.Path.GetExtension({access}.Name).TrimStart('.'), \"{Escape(ext)}\", global::System.StringComparison.OrdinalIgnoreCase)")
							.ToList();
						var nullGuard = isNullable ? $"{varName} != null && " : "";
						var displayExts = string.Join(", ", fe.Extensions);
						sb.AppendLine($"\t\t\tif ({nullGuard}({string.Join(" && ", extChecks)}))");
						sb.AppendLine("\t\t\t{");
						sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: --{Escape(cliName)}: extension must be one of: {Escape(displayExts)}.\");");
						EmitValidationErrorFooter(sb, p, cliName, "\t\t\t\t", flagHelpStdErrMethodName, runHint);
						sb.AppendLine($"\t\t\t\t{failureExit};");
						sb.AppendLine("\t\t\t}");
						break;
					}
				}
			}
		}
	}

	private static bool IsCollectionFilesystemConstraint(ValidationConstraint c) =>
		c is ExistingPathConstraint or NonExistingPathConstraint or RejectSymbolicLinksConstraint or FileExtensionsConstraint;

	/// <summary>
	/// Collection-aware emission for the filesystem-path attribute family (<c>[Existing]</c>, <c>[NonExisting]</c>,
	/// <c>[RejectSymbolicLinks]</c>, <c>[FileExtensions]</c>) applied to a <c>List&lt;FileInfo&gt;</c>,
	/// <c>FileInfo[]</c>, <c>DirectoryInfo[]</c>, etc. — including variadic <c>[Argument]</c> collections.
	/// Unlike the scalar constraint switch above (which exits on the first violation), this loops over every
	/// element and collects every failing item before printing one error block and exiting once, so a user
	/// passing e.g. five files with two missing sees both, not just the first.
	/// </summary>
	private static void EmitCollectionFilesystemValidation(
		StringBuilder sb, ParameterModel p, string cliName, string varName,
		string failureExit, string? flagHelpStdErrMethodName, string? runHint)
	{
		if (p.Validations.IsDefaultOrEmpty)
			return;
		var fsConstraints = p.Validations.Where(IsCollectionFilesystemConstraint).ToList();
		if (fsConstraints.Count == 0)
			return;

		var isDir = p.ElementScalarKind == CliScalarKind.DirectoryInfo;
		var failuresVar = "__fsFailures_" + p.LocalVarName;
		var itemVar = "__fsItem_" + p.LocalVarName;
		var msgVar = "__fsMsg_" + p.LocalVarName;
		var argToken = p.Kind == ParameterKind.Positional ? $"<{Escape(cliName)}>" : $"--{Escape(cliName)}";
		var nullGuard = !p.IsRequired && p.DeclaredNullableAnnotated;
		const string outerIndent = "\t\t\t";
		var loopIndent = nullGuard ? outerIndent + "\t" : outerIndent;
		var bodyIndent = loopIndent + "\t";

		sb.AppendLine($"{outerIndent}var {failuresVar} = new List<string>();");
		if (nullGuard)
		{
			sb.AppendLine($"{outerIndent}if ({varName} != null)");
			sb.AppendLine($"{outerIndent}{{");
		}

		sb.AppendLine($"{loopIndent}foreach (var {itemVar} in {varName})");
		sb.AppendLine($"{loopIndent}{{");

		foreach (var c in fsConstraints)
		{
			switch (c)
			{
				case RejectSymbolicLinksConstraint:
					// Runs before existence/extension checks; a rejected symlink skips further checks for that item.
					sb.AppendLine($"{bodyIndent}if (global::Nullean.Argh.ArghIO.PathIsSymbolicOrReparsePoint({itemVar}.FullName))");
					sb.AppendLine($"{bodyIndent}{{");
					sb.AppendLine($"{bodyIndent}\t{failuresVar}.Add({itemVar}.FullName + \": path must not be a symbolic link or reparse point.\");");
					sb.AppendLine($"{bodyIndent}\tcontinue;");
					sb.AppendLine($"{bodyIndent}}}");
					break;
				case ExistingPathConstraint:
					if (isDir)
					{
						sb.AppendLine($"{bodyIndent}if (!global::System.IO.Directory.Exists({itemVar}.FullName))");
						sb.AppendLine($"{bodyIndent}\t{failuresVar}.Add({itemVar}.FullName + \": directory does not exist.\");");
					}
					else
					{
						sb.AppendLine($"{bodyIndent}if (!global::System.IO.File.Exists({itemVar}.FullName))");
						sb.AppendLine($"{bodyIndent}\t{failuresVar}.Add({itemVar}.FullName + \": file does not exist.\");");
					}
					break;
				case NonExistingPathConstraint:
					sb.AppendLine($"{bodyIndent}if (global::System.IO.File.Exists({itemVar}.FullName) || global::System.IO.Directory.Exists({itemVar}.FullName))");
					sb.AppendLine($"{bodyIndent}\t{failuresVar}.Add({itemVar}.FullName + \": path already exists.\");");
					break;
				case FileExtensionsConstraint fe:
				{
					var extChecks = fe.Extensions
						.Select(ext => $"!string.Equals(global::System.IO.Path.GetExtension({itemVar}.Name).TrimStart('.'), \"{Escape(ext)}\", global::System.StringComparison.OrdinalIgnoreCase)")
						.ToList();
					var displayExts = string.Join(", ", fe.Extensions);
					sb.AppendLine($"{bodyIndent}if ({string.Join(" && ", extChecks)})");
					sb.AppendLine($"{bodyIndent}\t{failuresVar}.Add({itemVar}.FullName + \": extension must be one of: {Escape(displayExts)}.\");");
					break;
				}
			}
		}

		sb.AppendLine($"{loopIndent}}}");
		if (nullGuard)
			sb.AppendLine($"{outerIndent}}}");

		sb.AppendLine($"{outerIndent}if ({failuresVar}.Count > 0)");
		sb.AppendLine($"{outerIndent}{{");
		sb.AppendLine($"{outerIndent}\tforeach (var {msgVar} in {failuresVar})");
		sb.AppendLine($"{outerIndent}\t\tConsole.Error.WriteLine(\"Error: {argToken}: \" + {msgVar});");
		EmitValidationErrorFooter(sb, p, cliName, outerIndent + "\t", flagHelpStdErrMethodName, runHint);
		sb.AppendLine($"{outerIndent}\t{failureExit};");
		sb.AppendLine($"{outerIndent}}}");
	}

	private static string EscapeVerbatimString(string s) => s.Replace("\"", "\"\"");

	private static void EmitCommandRunnerFuzzyFailHelper(
		StringBuilder sb,
		CommandModel cmd,
		string? flagHelpStdErrMethodName,
		string? parseFailureRunHint)
	{
		var flagParams = cmd.Parameters
			.Where(static p => IsEmittedFlagLike(p.Kind))
			.ToList();

		if (flagParams.Count > 0)
		{
			sb.Append("\t\t\tvar __flagFuzzyCands = new string[] { ");
			var sortedNames = flagParams
				.Select(static p => p.CliLongName)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
				.ToList();
			for (var i = 0; i < sortedNames.Count; i++)
			{
				if (i > 0)
					sb.Append(", ");
				sb.Append('"').Append(Escape(sortedNames[i])).Append('"');
			}
			sb.AppendLine(" };");
		}

		sb.AppendLine("\t\t\tint FailUnknownLongOption(string flagName)");
		sb.AppendLine("\t\t\t{");
		if (flagParams.Count > 0)
		{
			sb.AppendLine($"\t\t\t\tvar __matches = FuzzyMatch.FindClosest(flagName, __flagFuzzyCands, {FuzzyMaxDistance});");
			sb.AppendLine("\t\t\t\tif (__matches.Count == 0)");
			sb.AppendLine("\t\t\t\t{");
		}
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'.\");");
		if (parseFailureRunHint is not null)
		{
			sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
			sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine(\"{Escape(parseFailureRunHint)}\");");
		}
		sb.AppendLine("\t\t\t\t\treturn 2;");
		if (flagParams.Count > 0)
		{
			sb.AppendLine("\t\t\t\t}");
			sb.AppendLine("\t\t\t\tif (__matches.Count == 1)");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\tvar __m = __matches[0];");
			sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'. Did you mean '--{__m}'?\");");
			if (flagHelpStdErrMethodName is not null)
			{
				sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
				sb.AppendLine($"\t\t\t\t\t{flagHelpStdErrMethodName}(__m);");
			}
			if (parseFailureRunHint is not null)
			{
				sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
				sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine(\"{Escape(parseFailureRunHint)}\");");
			}
			sb.AppendLine("\t\t\t\t\treturn 2;");
			sb.AppendLine("\t\t\t\t}");
			sb.AppendLine("\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'. Did you mean one of these?\");");
			sb.AppendLine("\t\t\t\tConsole.Error.WriteLine();");
			sb.AppendLine("\t\t\t\tforeach (var __m in __matches)");
			sb.AppendLine("\t\t\t\t{");
			if (flagHelpStdErrMethodName is not null)
				sb.AppendLine($"\t\t\t\t\t{flagHelpStdErrMethodName}(__m);");
			sb.AppendLine("\t\t\t\t}");
			if (parseFailureRunHint is not null)
			{
				sb.AppendLine("\t\t\t\tConsole.Error.WriteLine();");
				sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"{Escape(parseFailureRunHint)}\");");
			}
			sb.AppendLine("\t\t\t\treturn 2;");
		}
		sb.AppendLine("\t\t\t}");
		sb.AppendLine();
	}

}
