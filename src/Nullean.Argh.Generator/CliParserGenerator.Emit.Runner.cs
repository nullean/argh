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
	private static void EmitCommandRunner(
		StringBuilder sb,
		CommandModel cmd,
		ImmutableArray<GlobalMiddlewareRegistration> globalMiddleware,
		bool emitDtoTryParse = false,
		bool dtoLenient = false,
		string? dtoMethodName = null,
		string? dtoResultTypeFq = null,
		string? dtoOptionsTypeFq = null,
		ImmutableArray<string>? dtoOptionsBestCtorParamOrder = null,
		ImmutableArray<(string TypeFq, string TypeMetadataName, ImmutableArray<string> AllBaseTypeMetadataNames, string StaticFieldName, string LocalVarName, ImmutableArray<ParameterModel> FlatMembers, ImmutableArray<string>? BestCtorParamOrder)> injectedOptions = default,
		string? entryAssemblyName = null)
	{
		var anyRepeatedCollection = cmd.Parameters.Any(static p =>
			p is { IsCollection: true, Kind: ParameterKind.Flag } && p.CollectionSeparator is null);

		var failureExit = emitDtoTryParse ? "return false" : "return 2";
		var helpMethodName = emitDtoTryParse ? null : HelpPrinterMethodForCommand(cmd);
		var flagHelpStdErrMethodName = emitDtoTryParse || cmd.IsRootDefault ? null : $"PrintHelp_{cmd.RunMethodName}_Flag_ToStdErr";
		string? parseFailureRunHint = null;
		if (!emitDtoTryParse && entryAssemblyName is not null && !string.IsNullOrEmpty(cmd.CommandName))
		{
			var routeForHint = cmd.RoutePrefix.IsDefaultOrEmpty ? "" : string.Join(" ", cmd.RoutePrefix) + " ";
			parseFailureRunHint = $"Run '{Escape(entryAssemblyName)} {Escape(routeForHint)}{Escape(cmd.CommandName)} --help' for usage.";
		}

		if (emitDtoTryParse)
		{
			if (dtoMethodName is null || dtoResultTypeFq is null)
				throw new InvalidOperationException("DTO try-parse requires method name and result type.");

			sb.AppendLine($"\t\tinternal static bool {dtoMethodName}(string[] args, out {dtoResultTypeFq}? value)");
			sb.AppendLine("\t\t{");
			sb.AppendLine("\t\t\tvalue = null;");
		}
		else
		{
			sb.AppendLine($"\t\tprivate static async Task<int> {cmd.RunMethodName}(string[] args, CancellationToken ct)");
			sb.AppendLine("\t\t{");
			sb.AppendLine("\t\t\tfor (var i = 0; i < args.Length; i++)");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tif (args[i] == \"--help\" || args[i] == \"-h\")");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine($"\t\t\t\t\t{helpMethodName}();");
			sb.AppendLine("\t\t\t\t\treturn 0;");
			sb.AppendLine("\t\t\t\t}");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine();
		}

		EmitCliValueDeclarations(sb, cmd, dtoOptionsTypeFq);

		sb.AppendLine("\t\t\tvar flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);");
		if (anyRepeatedCollection)
		{
			sb.AppendLine("\t\t\tvar multiFlags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);");
			EmitIsMultiFlagPredicate(sb, cmd);
			sb.AppendLine("\t\t\tvoid SetFlag(string name, string? value)");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tif (IsMultiFlag(name))");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\tif (value is null) return;");
			sb.AppendLine("\t\t\t\t\tif (!multiFlags.TryGetValue(name, out var list)) { list = new List<string>(); multiFlags[name] = list; }");
			sb.AppendLine("\t\t\t\t\tlist.Add(value);");
			sb.AppendLine("\t\t\t\t\treturn;");
			sb.AppendLine("\t\t\t\t}");
			sb.AppendLine("\t\t\t\tflags[name] = value;");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine("\t\t\tvar positionals = new List<string>();");
		EmitBoolSwitchNames(sb, cmd);
		EmitCanonFlagNameMethod(sb, cmd);
		EmitShortFlagMethods(sb, cmd, multiFlagsAvailable: anyRepeatedCollection,
			parseFailureRunHint: emitDtoTryParse ? null : parseFailureRunHint);
		EmitKnownNonBoolFlagNames(sb, cmd);
		if (!emitDtoTryParse)
			EmitCommandRunnerFuzzyFailHelper(sb, cmd, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine("\t\t\tfor (var i = 0; i < args.Length;)");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tvar a = args[i];");
		sb.AppendLine("\t\t\t\tif (a.StartsWith(\"--\", StringComparison.Ordinal))");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\tvar eq = a.IndexOf('=');");
		sb.AppendLine("\t\t\t\t\tif (eq >= 0)");
		sb.AppendLine("\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\tvar flagName = CanonFlagName(a.Substring(2, eq - 2));");
		sb.AppendLine("\t\t\t\t\t\tvar flagValue = a.Substring(eq + 1);");
		if (emitDtoTryParse && !dtoLenient)
		{
			sb.AppendLine("\t\t\t\t\t\tif (!IsBoolSwitchName(flagName) && !IsKnownNonBoolFlagName(flagName))");
			sb.AppendLine("\t\t\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'.\");");
			sb.AppendLine($"\t\t\t\t\t\t\t{failureExit};");
			sb.AppendLine("\t\t\t\t\t\t}");
		}
		else if (!emitDtoTryParse)
		{
			sb.AppendLine("\t\t\t\t\t\tif (!IsBoolSwitchName(flagName) && !IsKnownNonBoolFlagName(flagName))");
			sb.AppendLine("\t\t\t\t\t\t\treturn FailUnknownLongOption(flagName);");
		}
		if (anyRepeatedCollection)
		{
			sb.AppendLine("\t\t\t\t\t\tSetFlag(flagName, flagValue);");
		}
		else
		{
			sb.AppendLine("\t\t\t\t\t\tflags[flagName] = flagValue;");
		}

		sb.AppendLine("\t\t\t\t\t\ti++;");
		sb.AppendLine("\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\telse");
		sb.AppendLine("\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\tvar flagName = CanonFlagName(a.Substring(2));");
		sb.AppendLine("\t\t\t\t\t\tif (IsBoolSwitchName(flagName))");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tflags[flagName] = IsBoolSwitchNoName(flagName) ? null : \"true\";");
		sb.AppendLine("\t\t\t\t\t\t\ti++;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\telse");
		sb.AppendLine("\t\t\t\t\t\t{");
		if (emitDtoTryParse)
		{
			if (dtoLenient)
			{
				sb.AppendLine("\t\t\t\t\t\t\tif (!IsKnownNonBoolFlagName(flagName))");
				sb.AppendLine("\t\t\t\t\t\t\t{");
				sb.AppendLine("\t\t\t\t\t\t\t\tif (i + 1 < args.Length && !args[i + 1].StartsWith(\"-\", StringComparison.Ordinal))");
				sb.AppendLine("\t\t\t\t\t\t\t\t\ti += 2;");
				sb.AppendLine("\t\t\t\t\t\t\t\telse");
				sb.AppendLine("\t\t\t\t\t\t\t\t\ti++;");
				sb.AppendLine("\t\t\t\t\t\t\t\tcontinue;");
				sb.AppendLine("\t\t\t\t\t\t\t}");
			}
			else
			{
				sb.AppendLine("\t\t\t\t\t\t\tif (!IsKnownNonBoolFlagName(flagName))");
				sb.AppendLine("\t\t\t\t\t\t\t{");
				sb.AppendLine("\t\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'.\");");
				sb.AppendLine($"\t\t\t\t\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t\t\t\t\t}");
			}
		}
		else
		{
			sb.AppendLine("\t\t\t\t\t\t\tif (!IsKnownNonBoolFlagName(flagName))");
			sb.AppendLine("\t\t\t\t\t\t\t\treturn FailUnknownLongOption(flagName);");
		}
		sb.AppendLine("\t\t\t\t\t\t\tif (i + 1 >= args.Length)");
		sb.AppendLine("\t\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: missing value for flag --{flagName}.\");");
		if (flagHelpStdErrMethodName is not null)
		{
			sb.AppendLine("\t\t\t\t\t\t\t\tConsole.Error.WriteLine();");
			sb.AppendLine($"\t\t\t\t\t\t\t\t{flagHelpStdErrMethodName}(flagName);");
			sb.AppendLine("\t\t\t\t\t\t\t\tConsole.Error.WriteLine();");
			if (parseFailureRunHint is not null)
				sb.AppendLine($"\t\t\t\t\t\t\t\tConsole.Error.WriteLine(\"{parseFailureRunHint}\");");
		}
		else if (helpMethodName is not null)
			sb.AppendLine($"\t\t\t\t\t\t\t\t{helpMethodName}();");
		sb.AppendLine($"\t\t\t\t\t\t\t\t{failureExit};");
		sb.AppendLine("\t\t\t\t\t\t\t}");
		if (anyRepeatedCollection)
		{
			sb.AppendLine("\t\t\t\t\t\t\tSetFlag(flagName, args[i + 1]);");
		}
		else
		{
			sb.AppendLine("\t\t\t\t\t\t\tflags[flagName] = args[i + 1];");
		}

		sb.AppendLine("\t\t\t\t\t\t\ti += 2;");
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
		if (helpMethodName is not null)
			sb.AppendLine($"\t\t\t\t\t\t\t{helpMethodName}();");
		sb.AppendLine($"\t\t\t\t\t\t\t{failureExit};");
		sb.AppendLine("\t\t\t\t\t\t}");
		if (emitDtoTryParse && dtoLenient)
			sb.AppendLine("\t\t\t\t\t\tTryApplyShortFlag(shortKey[0], a.Substring(eqs + 1));");
		else
		{
			sb.AppendLine("\t\t\t\t\t\tif (!TryApplyShortFlag(shortKey[0], a.Substring(eqs + 1)))");
			sb.AppendLine($"\t\t\t\t\t\t\t{failureExit};");
		}
		sb.AppendLine("\t\t\t\t\t\ti++;");
		sb.AppendLine("\t\t\t\t\t\tcontinue;");
		sb.AppendLine("\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\tif (a.Length == 2)");
		sb.AppendLine("\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\tvar sc = a[1];");
		sb.AppendLine("\t\t\t\t\t\tif (IsShortBoolChar(sc))");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tif (!TryApplyShortFlag(sc, \"true\"))");
		sb.AppendLine($"\t\t\t\t\t\t\t\t{failureExit};");
		sb.AppendLine("\t\t\t\t\t\t\ti++;");
		sb.AppendLine("\t\t\t\t\t\t\tcontinue;");
		sb.AppendLine("\t\t\t\t\t\t}");
		if (emitDtoTryParse && dtoLenient)
		{
			// lenient: skip unknown value-taking short flags using same heuristic as long flags
			sb.AppendLine("\t\t\t\t\t\tif (i + 1 < args.Length && !args[i + 1].StartsWith(\"-\", StringComparison.Ordinal))");
			sb.AppendLine("\t\t\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\t\t\tTryApplyShortFlag(sc, args[i + 1]);");
			sb.AppendLine("\t\t\t\t\t\t\ti += 2;");
			sb.AppendLine("\t\t\t\t\t\t}");
			sb.AppendLine("\t\t\t\t\t\telse");
			sb.AppendLine("\t\t\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\t\t\tTryApplyShortFlag(sc, \"true\");");
			sb.AppendLine("\t\t\t\t\t\t\ti++;");
			sb.AppendLine("\t\t\t\t\t\t}");
			sb.AppendLine("\t\t\t\t\t\tcontinue;");
		}
		else
		{
			sb.AppendLine("\t\t\t\t\t\tif (i + 1 >= args.Length)");
			sb.AppendLine("\t\t\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: missing value for short flag '-{sc}'.\");");
			if (helpMethodName is not null)
				sb.AppendLine($"\t\t\t\t\t\t\t{helpMethodName}();");
			sb.AppendLine($"\t\t\t\t\t\t\t{failureExit};");
			sb.AppendLine("\t\t\t\t\t\t}");
			sb.AppendLine("\t\t\t\t\t\tif (!TryApplyShortFlag(sc, args[i + 1]))");
			sb.AppendLine($"\t\t\t\t\t\t\t{failureExit};");
			sb.AppendLine("\t\t\t\t\t\ti += 2;");
			sb.AppendLine("\t\t\t\t\t\tcontinue;");
		}
		sb.AppendLine("\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine(\"Error: combined short flags (e.g. -abc) are not supported.\");");
		if (helpMethodName is not null)
			sb.AppendLine($"\t\t\t\t\t{helpMethodName}();");
		sb.AppendLine($"\t\t\t\t\t{failureExit};");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\tpositionals.Add(a);");
		sb.AppendLine("\t\t\t\ti++;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine();

		foreach (var p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Injected || p.Kind == ParameterKind.OptionsInjected)
				continue;

			if (p.Kind == ParameterKind.Positional)
				continue;

			if (p.Special == BoolSpecialKind.Bool)
			{
				sb.AppendLine($"\t\t\tvar {p.LocalVarName} = flags.ContainsKey(\"{Escape(p.CliLongName)}\");");
				continue;
			}

			if (p.Special == BoolSpecialKind.NullableBool)
			{
				sb.AppendLine($"\t\t\tbool? {p.LocalVarName} = null;");
				sb.AppendLine($"\t\t\tif (flags.TryGetValue(\"{Escape(p.CliLongName)}\", out var {p.LocalVarName}_yesVal))");
				sb.AppendLine($"\t\t\t\t{p.LocalVarName} = ParseNullableBool({p.LocalVarName}_yesVal, true);");
				sb.AppendLine($"\t\t\tif (flags.TryGetValue(\"no-{Escape(p.CliLongName)}\", out var {p.LocalVarName}_noVal))");
				sb.AppendLine($"\t\t\t\t{p.LocalVarName} = ParseNullableBool({p.LocalVarName}_noVal, false);");
				continue;
			}

			if (p.IsCollection && p.Kind == ParameterKind.Flag)
			{
				EmitBindCollectionParameter(sb, p, anyRepeatedCollection, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
				continue;
			}

			var flagKey = Escape(p.CliLongName);
			sb.AppendLine($"\t\t\tif (!flags.TryGetValue(\"{flagKey}\", out var {p.LocalVarName}Text))");
			if (p.IsRequired)
			{
				sb.AppendLine("\t\t\t{");
				sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine($\"Error: missing required flag --{flagKey}.\");");
				EmitAfterCliParseErrorHelp(sb, p, "\t\t\t\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
				sb.AppendLine($"\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t}");
			}
			else
				sb.AppendLine($"\t\t\t\t{p.LocalVarName}Text = null;");

			EmitParseAndAssign(sb, p, p.LocalVarName + "Text", p.LocalVarName, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		}

		var posIndex = 0;
		foreach (var p in cmd.Parameters)
		{
			if (p.Kind != ParameterKind.Positional)
				continue;

			if (p.IsVariadic)
			{
				EmitVariadicPositionalParse(sb, p, posIndex, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
				posIndex++;
				continue;
			}

			if (p.IsRequired)
			{
				sb.AppendLine($"\t\t\tif (positionals.Count <= {posIndex})");
				sb.AppendLine("\t\t\t{");
				sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: missing required argument <{Escape(p.CliLongName)}>.\");");
				if (helpMethodName is not null)
					sb.AppendLine($"\t\t\t\t{helpMethodName}();");
				sb.AppendLine($"\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t}");
				sb.AppendLine("\t\t\telse");
				sb.AppendLine("\t\t\t{");
				EmitParseFromString(sb, p, $"positionals[{posIndex}]", p.LocalVarName, indentExtra: "\t", failureExit: failureExit, helpMethodName: helpMethodName, flagHelpStdErrMethodName: flagHelpStdErrMethodName, parseFailureRunHint: parseFailureRunHint);
				sb.AppendLine("\t\t\t}");
			}
			else
			{
				var fallback = p.DefaultValueLiteral ?? "default!";
				sb.AppendLine($"\t\t\tif (positionals.Count <= {posIndex})");
				sb.AppendLine($"\t\t\t\t{p.LocalVarName} = {fallback};");
				sb.AppendLine("\t\t\telse");
				sb.AppendLine("\t\t\t{");
				EmitParseFromString(sb, p, $"positionals[{posIndex}]", p.LocalVarName, indentExtra: "\t", failureExit: failureExit, helpMethodName: helpMethodName, flagHelpStdErrMethodName: flagHelpStdErrMethodName, parseFailureRunHint: parseFailureRunHint);
				sb.AppendLine("\t\t\t}");
			}

			posIndex++;
		}

		if (emitDtoTryParse)
		{
			EmitValidationChecks(sb, cmd, failureExit, entryAssemblyName: null);
			if (dtoOptionsTypeFq is not null)
				EmitOptionsDtoConstructionAndReturn(sb, dtoOptionsTypeFq, cmd.Parameters, dtoOptionsBestCtorParamOrder);
			else
				EmitAsParametersConstructionForDto(sb, cmd);

			sb.AppendLine("\t\t}");
			sb.AppendLine();
			return;
		}

		EmitAsParametersConstruction(sb, cmd);

		EmitValidationChecks(sb, cmd, failureExit, entryAssemblyName, flagHelpStdErrMethodName);

		// Reconstruct options instances merging command-level flags with pre-parsed statics.
		EmitOptionsReconstructLocals(sb, injectedOptions);

		if (cmd.RequiresInstance)
		{
			// Try to construct from options-injected ctor parameters before falling back to DI or parameterless ctor.
			string? optionsCtorArgs = null;
			if (!injectedOptions.IsDefaultOrEmpty && !cmd.ContainingTypeCtorParams.IsDefaultOrEmpty)
			{
				var ctorParams = cmd.ContainingTypeCtorParams;
				if (ctorParams.Length > 0)
				{
					var ctorArgs = new List<string>();
					var allResolved = true;
					foreach (var (_, cpMetaName) in ctorParams)
					{
						// Exact match first, then most-derived (from end of chain); use LocalVarName (reconstructed)
						string? bestLocal = null;
						foreach (var o in injectedOptions)
							if (o.TypeMetadataName == cpMetaName) { bestLocal = o.LocalVarName; break; }
						if (bestLocal is null)
							for (var _i = injectedOptions.Length - 1; _i >= 0; _i--)
								if (injectedOptions[_i].AllBaseTypeMetadataNames.Contains(cpMetaName)) { bestLocal = injectedOptions[_i].LocalVarName; break; }
						if (bestLocal is null) { allResolved = false; break; }
						ctorArgs.Add(bestLocal);
					}
					if (allResolved)
						optionsCtorArgs = string.Join(", ", ctorArgs);
				}
			}

			if (optionsCtorArgs is not null)
			{
				sb.AppendLine(
					$"\t\t\tvar __cmdHandler = (ArghServices.ServiceProvider?.GetService(typeof({cmd.ContainingTypeFq})) as {cmd.ContainingTypeFq}) ?? new {cmd.ContainingTypeFq}({optionsCtorArgs});");
			}
			else if (cmd.ContainingTypeHasParameterlessCtor)
			{
				sb.AppendLine(
					$"\t\t\tvar __cmdHandler = (ArghServices.ServiceProvider?.GetService(typeof({cmd.ContainingTypeFq})) as {cmd.ContainingTypeFq}) ?? new {cmd.ContainingTypeFq}();");
			}
			else
			{
				sb.AppendLine(
					$"\t\t\tvar __cmdHandler = (ArghServices.ServiceProvider?.GetService(typeof({cmd.ContainingTypeFq})) as {cmd.ContainingTypeFq}) ?? throw new global::System.InvalidOperationException(\"Register the command type in DI for hosted execution, or add a public parameterless constructor for standalone CLI.\");");
			}

			sb.AppendLine();
		}

		sb.AppendLine();
		var useMiddleware = globalMiddleware.Length > 0 || cmd.CommandMiddlewareData.Length > 0;
		if (!useMiddleware)
		{
			sb.Append("\t\t\t");
			EmitInvocation(sb, cmd, injectedOptions: injectedOptions);
			sb.AppendLine();
		}
		else
		{
			EmitCommandPathLiteral(sb, cmd);
			sb.AppendLine("\t\t\tvar ctx = new CommandContext(commandPath, args, ct);");
			sb.AppendLine("\t\t\tCommandMiddlewareDelegate next = async c =>");
			sb.AppendLine("\t\t\t{");
			EmitInvocation(sb, cmd, "c.CancellationToken", "c", "\t\t\t\t", injectedOptions: injectedOptions);
			sb.AppendLine("\t\t\t};");
			var cap = 0;
			for (var i = cmd.CommandMiddlewareData.Length - 1; i >= 0; i--)
			{
				var (fq, middlewareParamless) = cmd.CommandMiddlewareData[i];
				var name = "__cap" + cap++;
				sb.AppendLine($"\t\t\tvar {name} = next;");
				sb.AppendLine($"\t\t\tnext = async c => await {DiResolveOrNew(fq, middlewareParamless)}.InvokeAsync(c, {name});");
			}

			for (var i = globalMiddleware.Length - 1; i >= 0; i--)
			{
				var gFq = globalMiddleware[i].TypeFq;
				var gParamless = globalMiddleware[i].HasParameterlessCtor;
				var name = "__cap" + cap++;
				sb.AppendLine($"\t\t\tvar {name} = next;");
				sb.AppendLine($"\t\t\tnext = async c => await {DiResolveOrNew(gFq, gParamless)}.InvokeAsync(c, {name});");
			}

			sb.AppendLine("\t\t\tawait next(ctx).ConfigureAwait(false);");
			sb.AppendLine("\t\t\treturn ctx.ExitCode;");
		}

		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	private static void EmitCommandPathLiteral(StringBuilder sb, CommandModel cmd)
	{
		sb.Append("\t\t\tvar commandPath = new string[] { ");
		for (var i = 0; i < cmd.RoutePrefix.Length; i++)
		{
			if (i > 0)
				sb.Append(", ");
			sb.Append('"').Append(Escape(cmd.RoutePrefix[i])).Append('"');
		}

		if (cmd.RoutePrefix.Length > 0)
			sb.Append(", ");
		sb.Append('"').Append(Escape(cmd.CommandName)).Append('"');
		sb.AppendLine(" };");
	}

	private static void EmitCliValueDeclarations(StringBuilder sb, CommandModel cmd, string? rtDefaultTypeFq = null)
	{
		// For cross-assembly options types, seed non-nullable properties from a runtime instance.
		// rtDefaultTypeFq covers UseGlobalOptions / UseNamespaceOptions DTO paths.
		// AsParametersTypeFq covers [AsParameters] init-property paths from cross-assembly types.
		var hasRtDefaults = rtDefaultTypeFq is not null && cmd.Parameters.Any(static p => p.UsesRuntimeDefault);
		if (hasRtDefaults)
			sb.AppendLine($"\t\t\tvar __rt_default = new {rtDefaultTypeFq}();");

		// Emit one runtime-default instance per unique cross-assembly [AsParameters] type.
		var asParamsRtTypes = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var p in cmd.Parameters)
		{
			if (!p.UsesRuntimeDefault || p.AsParametersTypeFq is null)
				continue;
			if (asParamsRtTypes.ContainsKey(p.AsParametersTypeFq))
				continue;
			var suffix = DtoMethodSuffix(p.AsParametersTypeFq);
			asParamsRtTypes[p.AsParametersTypeFq] = suffix;
			sb.AppendLine($"\t\t\tvar __rt_default_{suffix} = new {p.AsParametersTypeFq}();");
		}

		foreach (var p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Injected || p.Kind == ParameterKind.OptionsInjected)
				continue;

			if (p.Special == BoolSpecialKind.Bool || p.Special == BoolSpecialKind.NullableBool)
				continue;

			if (p.IsCollection && p.Kind == ParameterKind.Flag)
			{
				var elemFq = GetElementCSharpFq(p);
				var accType = p.CollectionTargetIsReadOnlySet
					? "global::System.Collections.Generic.HashSet"
					: "global::System.Collections.Generic.List";
				sb.AppendLine(
					$"\t\t\tvar {p.LocalVarName}_acc = new {accType}<{elemFq}>();");
				continue;
			}

			string initializer;
			if (p.UsesRuntimeDefault)
			{
				if (hasRtDefaults && rtDefaultTypeFq is not null)
					initializer = $"__rt_default.{p.SymbolName}";
				else if (p.AsParametersTypeFq is not null && asParamsRtTypes.TryGetValue(p.AsParametersTypeFq, out var asParamsSuffix))
					initializer = $"__rt_default_{asParamsSuffix}.{p.SymbolName}";
				else
					initializer = GetCliInitializer(p);
			}
			else
				initializer = GetCliInitializer(p);

			sb.AppendLine($"\t\t\t{GetCSharpCliType(p)} {p.LocalVarName} = {initializer};");
		}
	}

	private static string GetElementCSharpFq(ParameterModel p)
	{
		switch (p.ElementScalarKind)
		{
			case CliScalarKind.Enum when p.ElementEnumTypeFq is not null:
				return p.ElementEnumTypeFq;
			case CliScalarKind.FileInfo:
				return "global::System.IO.FileInfo";
			case CliScalarKind.DirectoryInfo:
				return "global::System.IO.DirectoryInfo";
			case CliScalarKind.Uri:
				return "global::System.Uri";
			case CliScalarKind.CustomParser when p.ElementCustomValueTypeFq is not null:
				return p.ElementCustomValueTypeFq;
			default:
				break;
		}

		return p.ElementTypeName switch
		{
			"string" => "string",
			"int" => "int",
			"long" => "long",
			"float" => "float",
			"double" => "double",
			"decimal" => "decimal",
			"bool" => "bool",
			"DateTime" => "global::System.DateTime",
			"DateTimeOffset" => "global::System.DateTimeOffset",
			"TimeSpan" => "global::System.TimeSpan",
			"DateOnly" => "global::System.DateOnly",
			"DateTime?" => "global::System.DateTime?",
			"DateTimeOffset?" => "global::System.DateTimeOffset?",
			"TimeSpan?" => "global::System.TimeSpan?",
			"DateOnly?" => "global::System.DateOnly?",
			_ => "string"
		};
	}

	private static ParameterModel ForElementParsing(ParameterModel p) =>
		p with
		{
			ScalarKind = p.ElementScalarKind,
			TypeName = p.ElementTypeName,
			EnumTypeFq = p.ElementEnumTypeFq,
			EnumMemberNames = p.ElementEnumMemberNames,
			EnumMemberCliNames = p.ElementEnumMemberCliNames,
			ParserTypeFq = p.ElementParserTypeFq,
			CustomValueTypeFq = p.ElementCustomValueTypeFq,
			Special = BoolSpecialKind.None,
			IsCollection = false,
			IsRequired = true
		};

	private static void EmitVariadicPositionalParse(
		StringBuilder sb,
		ParameterModel p,
		int startIndex,
		string failureExit,
		string? helpMethodName,
		string? flagHelpStdErrMethodName,
		string? parseFailureRunHint)
	{
		var argName = Escape(p.CliLongName);
		var countVar = "__varCount_" + p.LocalVarName;
		var arrVar = "__arr_" + p.LocalVarName;
		var elemModel = ForElementParsing(p);
		var elemCsharpType = GetCSharpCliType(elemModel);

		// For [Argument] params T[] with no [MinLength], zero items is valid (C# params convention).
		// If the user added [MinLength(n)], CollectionCountConstraint validation below enforces at-least-n.
		// However if IsRequired is true (non-nullable, no default), we require at least 1.
		if (p.IsRequired)
		{
			sb.AppendLine($"\t\t\tif (positionals.Count <= {startIndex})");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"Error: missing required argument <{argName}...>.\");");
			if (helpMethodName is not null)
				sb.AppendLine($"\t\t\t\t{helpMethodName}();");
			sb.AppendLine($"\t\t\t\t{failureExit};");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine($"\t\t\tvar {countVar} = positionals.Count > {startIndex} ? positionals.Count - {startIndex} : 0;");
		sb.AppendLine($"\t\t\tvar {arrVar} = new {elemCsharpType}[{countVar}];");
		sb.AppendLine($"\t\t\tfor (var __vi_{p.LocalVarName} = 0; __vi_{p.LocalVarName} < {countVar}; __vi_{p.LocalVarName}++)");
		sb.AppendLine("\t\t\t{");

		if (p.ElementScalarKind == CliScalarKind.Primitive && p.ElementTypeName == "string")
		{
			sb.AppendLine($"\t\t\t\t{arrVar}[__vi_{p.LocalVarName}] = positionals[{startIndex} + __vi_{p.LocalVarName}];");
		}
		else
		{
			EmitParseFromString(sb, elemModel,
				$"positionals[{startIndex} + __vi_{p.LocalVarName}]",
				$"{arrVar}[__vi_{p.LocalVarName}]",
				indentExtra: "\t",
				outVarKeyword: false,
				failureExit: failureExit,
				helpMethodName: helpMethodName,
				flagHelpStdErrMethodName: flagHelpStdErrMethodName,
				parseFailureRunHint: parseFailureRunHint);
		}

		sb.AppendLine("\t\t\t}");
		sb.AppendLine($"\t\t\t{p.LocalVarName} = {arrVar};");
	}

	private static string GetCSharpCliType(ParameterModel p)
	{
		if (p.ScalarKind == CliScalarKind.Collection && p.FullDeclaredTypeFq is not null)
			return p.FullDeclaredTypeFq;

		switch (p.ScalarKind)
		{
			case CliScalarKind.Enum when p.EnumTypeFq is not null:
				// Optional on the CLI but backed by a non-nullable enum + default (e.g. options properties): keep a non-nullable temp.
				// Also keep non-nullable for cross-assembly runtime-default properties (no null initial value).
				// IsNullableAnnotated guards against NRT nullable enums (e.g. MyEnum?) on cross-assembly types.
				if ((p.IsRequired || p.DefaultValueLiteral is not null || p.UsesRuntimeDefault) && !p.IsNullableAnnotated)
					return p.EnumTypeFq;
				return p.EnumTypeFq + "?";
			case CliScalarKind.FileInfo:
				return (p.IsRequired || p.UsesRuntimeDefault) && !p.IsNullableAnnotated ? "global::System.IO.FileInfo" : "global::System.IO.FileInfo?";
			case CliScalarKind.DirectoryInfo:
				return (p.IsRequired || p.UsesRuntimeDefault) && !p.IsNullableAnnotated ? "global::System.IO.DirectoryInfo" : "global::System.IO.DirectoryInfo?";
			case CliScalarKind.Uri:
				return (p.IsRequired || p.UsesRuntimeDefault) && !p.IsNullableAnnotated ? "global::System.Uri" : "global::System.Uri?";
			case CliScalarKind.CustomParser when p.CustomValueTypeFq is not null:
				return (p.IsRequired || p.UsesRuntimeDefault) && !p.IsNullableAnnotated ? p.CustomValueTypeFq : p.CustomValueTypeFq + "?";
			default:
				break;
		}

		if (p.TypeName == "string")
			return (p.IsRequired || p.UsesRuntimeDefault) && !p.IsNullableAnnotated ? "string" : "string?";

		return p.TypeName switch
		{
			"int" => "int",
			"int?" => "int?",
			"long" => "long",
			"long?" => "long?",
			"float" => "float",
			"float?" => "float?",
			"double" => "double",
			"double?" => "double?",
			"decimal" => "decimal",
			"decimal?" => "decimal?",
			"bool" => "bool",
			"bool?" => "bool?",
			"DateTime" => "global::System.DateTime",
			"DateTime?" => "global::System.DateTime?",
			"DateTimeOffset" => "global::System.DateTimeOffset",
			"DateTimeOffset?" => "global::System.DateTimeOffset?",
			"TimeSpan" => "global::System.TimeSpan",
			"TimeSpan?" => "global::System.TimeSpan?",
			"DateOnly" => "global::System.DateOnly",
			"DateOnly?" => "global::System.DateOnly?",
			_ => "string?"
		};
	}

	private static string GetCliInitializer(ParameterModel p)
	{
		if (p.DefaultValueLiteral is not null)
			return p.DefaultValueLiteral;

		if (p.ScalarKind == CliScalarKind.Collection)
			return "null!";

		if (!p.IsRequired && p.ScalarKind is CliScalarKind.Enum or CliScalarKind.FileInfo or CliScalarKind.DirectoryInfo
		    or CliScalarKind.Uri or CliScalarKind.CustomParser)
			return "null";

		if (!p.IsRequired && p.TypeName.EndsWith("?", StringComparison.Ordinal))
			return "null";

		if (p.TypeName == "string")
			return p.IsRequired ? "null!" : "null";

		return "default!";
	}

	/// <summary>
	/// Global/namespace options flattened into a command via <see cref="FixOptionsParamsInCommands"/> use
	/// <see cref="ParameterKind.OptionsInjected"/> but must still participate in long-name aliases, short-option
	/// binding, and bare bool switch recognition the same as <see cref="ParameterKind.Flag"/>.
	/// </summary>
	private static bool IsEmittedFlagLike(ParameterKind kind) =>
		kind is ParameterKind.Flag or ParameterKind.OptionsInjected;

	private static void EmitBoolSwitchNames(StringBuilder sb, CommandModel cmd, bool suppressNoNameHelper = false)
	{
		var names = new List<string>();
		var noNames = new List<string>();
		foreach (var p in cmd.Parameters)
		{
			if (!IsEmittedFlagLike(p.Kind))
				continue;
			if (p.Special == BoolSpecialKind.Bool)
				names.Add(p.CliLongName);
			if (p.Special == BoolSpecialKind.NullableBool)
			{
				names.Add(p.CliLongName);
				noNames.Add("no-" + p.CliLongName);
			}
		}

		if (names.Count == 0 && noNames.Count == 0)
		{
			sb.AppendLine("\t\t\tbool IsBoolSwitchName(string name) => false;");
			if (!suppressNoNameHelper)
				sb.AppendLine("\t\t\tbool IsBoolSwitchNoName(string name) => false;");
			return;
		}

		var boolSwitchNameCases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var n in names)
			boolSwitchNameCases.Add(n);
		foreach (var n in noNames)
			boolSwitchNameCases.Add(n);

		sb.AppendLine("\t\t\tbool IsBoolSwitchName(string name) => name switch");
		sb.AppendLine("\t\t\t{");
		foreach (var n in boolSwitchNameCases.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
			sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");

		if (suppressNoNameHelper)
			return;
		if (noNames.Count == 0)
		{
			sb.AppendLine("\t\t\tbool IsBoolSwitchNoName(string name) => false;");
		}
		else
		{
			var noNameCases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var n in noNames)
				noNameCases.Add(n);
			sb.AppendLine("\t\t\tbool IsBoolSwitchNoName(string name) => name switch");
			sb.AppendLine("\t\t\t{");
			foreach (var n in noNameCases.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
				sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");
			sb.AppendLine("\t\t\t\t_ => false");
			sb.AppendLine("\t\t\t};");
		}
	}

	private static void EmitKnownNonBoolFlagNames(StringBuilder sb, CommandModel cmd)
	{
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var p in cmd.Parameters)
		{
			if (!IsEmittedFlagLike(p.Kind))
				continue;
			if (p.Special == BoolSpecialKind.Bool || p.Special == BoolSpecialKind.NullableBool)
				continue;
			names.Add(p.CliLongName);
			foreach (var al in p.Aliases)
				names.Add(al);
		}

		if (names.Count == 0)
		{
			sb.AppendLine("\t\t\tbool IsKnownNonBoolFlagName(string name) => false;");
			return;
		}

		sb.AppendLine("\t\t\tbool IsKnownNonBoolFlagName(string name) => name switch");
		sb.AppendLine("\t\t\t{");
		foreach (var n in names.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
			sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");
		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");
	}

	private static void EmitCanonFlagNameMethod(StringBuilder sb, CommandModel cmd)
	{
		var cases = new List<(string from, string to)>();
		foreach (var p in cmd.Parameters)
		{
			if (!IsEmittedFlagLike(p.Kind))
				continue;
			foreach (var al in p.Aliases)
			{
				if (string.Equals(al, p.CliLongName, StringComparison.OrdinalIgnoreCase))
					continue;
				cases.Add((al, p.CliLongName));
			}

			if (p.Special == BoolSpecialKind.NullableBool)
			{
				foreach (var al in p.Aliases)
				{
					if (string.Equals(al, p.CliLongName, StringComparison.OrdinalIgnoreCase))
						continue;
					cases.Add(("no-" + al, "no-" + p.CliLongName));
				}
			}
		}

		if (cases.Count == 0)
		{
			sb.AppendLine("\t\t\tstring CanonFlagName(string raw) => raw;");
			return;
		}

		sb.AppendLine("\t\t\tstring CanonFlagName(string raw) => raw switch");
		sb.AppendLine("\t\t\t{");
		foreach ((var from, var to) in cases)
			sb.AppendLine($"\t\t\t\t\"{Escape(from)}\" => \"{Escape(to)}\",");

		sb.AppendLine("\t\t\t\t_ => raw");
		sb.AppendLine("\t\t\t};");
	}

	private static void EmitShortFlagMethods(StringBuilder sb, CommandModel cmd, bool multiFlagsAvailable = true, string? parseFailureRunHint = null)
	{
		var shortCases = new List<(char c, string Primary, bool IsBool, bool IsRepeatableCollection)>();
		foreach (var p in cmd.Parameters)
		{
			if (!IsEmittedFlagLike(p.Kind))
				continue;
			if (p.ShortOpt is not char ch)
				continue;
			// IsRepeatableCollection only applies when multiFlags is available in the emitted context.
			var isRepeatableCollection = multiFlagsAvailable && p.IsCollection && p.CollectionSeparator is null;
			shortCases.Add((ch, p.CliLongName, p.Special == BoolSpecialKind.Bool, isRepeatableCollection));
		}

		if (shortCases.Count == 0)
		{
			sb.AppendLine("\t\t\tbool TryApplyShortFlag(char c, string val)");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tConsole.Error.WriteLine($\"Error: unknown short option '-{c}'.\");");
			if (parseFailureRunHint is not null)
			{
				sb.AppendLine("\t\t\t\tConsole.Error.WriteLine();");
				sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine(\"{Escape(parseFailureRunHint)}\");");
			}
			sb.AppendLine("\t\t\t\treturn false;");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine("\t\t\tbool IsShortBoolChar(char c) => false;");
			return;
		}

		sb.AppendLine("\t\t\tbool TryApplyShortFlag(char c, string val)");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tswitch (c)");
		sb.AppendLine("\t\t\t\t{");
		foreach ((var c, var primary, _, var isRepeatableCol) in shortCases)
		{
			var esc = Escape(primary);
			sb.AppendLine($"\t\t\t\t\tcase '{c}':");
			if (isRepeatableCol)
			{
				// Repeatable flag: append to multiFlags so short opt and long opt collect into the same list.
				sb.AppendLine($"\t\t\t\t\t\tif (!multiFlags.TryGetValue(\"{esc}\", out var __scList_{esc.Replace("-", "_")})) {{ __scList_{esc.Replace("-", "_")} = new List<string>(); multiFlags[\"{esc}\"] = __scList_{esc.Replace("-", "_")}; }}");
				sb.AppendLine($"\t\t\t\t\t\t__scList_{esc.Replace("-", "_")}.Add(val);");
			}
			else
			{
				sb.AppendLine($"\t\t\t\t\t\tflags[\"{esc}\"] = val;");
			}
			sb.AppendLine("\t\t\t\t\t\treturn true;");
		}

		sb.AppendLine("\t\t\t\t\tdefault:");
		sb.AppendLine("\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown short option '-{c}'.\");");
		if (parseFailureRunHint is not null)
		{
			sb.AppendLine("\t\t\t\t\t\tConsole.Error.WriteLine();");
			sb.AppendLine($"\t\t\t\t\t\tConsole.Error.WriteLine(\"{Escape(parseFailureRunHint)}\");");
		}
		sb.AppendLine("\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t}");

		var anyBool = shortCases.Exists(static x => x.IsBool);
		if (!anyBool)
		{
			sb.AppendLine("\t\t\tbool IsShortBoolChar(char c) => false;");
			return;
		}

		sb.AppendLine("\t\t\tbool IsShortBoolChar(char c) => c switch");
		sb.AppendLine("\t\t\t{");
		foreach ((var c, _, var isBool, _) in shortCases)
		{
			if (isBool)
				sb.AppendLine($"\t\t\t\t'{c}' => true,");
		}

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");
	}

	private static void EmitParseAndAssign(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string failureExit = "return 2", string? helpMethodName = null,
		string? flagHelpStdErrMethodName = null, string? parseFailureRunHint = null)
	{
		if (!p.IsRequired && p.DefaultValueLiteral is not null)
		{
			sb.AppendLine($"\t\t\tif ({rawExpr} is null)");
			sb.AppendLine($"\t\t\t\t{targetVar} = {p.DefaultValueLiteral};");
			sb.AppendLine("\t\t\telse");
			sb.AppendLine("\t\t\t{");
			EmitParseFromString(sb, p, rawExpr, targetVar, indentExtra: "\t", outVarKeyword: false, failureExit: failureExit, helpMethodName: helpMethodName, flagHelpStdErrMethodName: flagHelpStdErrMethodName, parseFailureRunHint: parseFailureRunHint);
			sb.AppendLine("\t\t\t}");
		}
		else if (!p.IsRequired && p.DefaultValueLiteral is null)
		{
			// Optional parameter with no explicit default: only parse when a value was actually provided.
			// Guards against passing null into type-specific parsers (e.g. Enum.TryParse) when the flag is absent.
			sb.AppendLine($"\t\t\tif ({rawExpr} is not null)");
			sb.AppendLine("\t\t\t{");
			EmitParseFromString(sb, p, rawExpr, targetVar, indentExtra: "\t", outVarKeyword: false, failureExit: failureExit, helpMethodName: helpMethodName, flagHelpStdErrMethodName: flagHelpStdErrMethodName, parseFailureRunHint: parseFailureRunHint);
			sb.AppendLine("\t\t\t}");
		}
		else
			EmitParseFromString(sb, p, rawExpr, targetVar, failureExit: failureExit, helpMethodName: helpMethodName, flagHelpStdErrMethodName: flagHelpStdErrMethodName, parseFailureRunHint: parseFailureRunHint);
	}

	private static void EmitParseFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string indentExtra = "",
		bool outVarKeyword = false, string failureExit = "return 2", string? helpMethodName = null, string? flagHelpStdErrMethodName = null, string? parseFailureRunHint = null)
	{
		var ind = "\t\t\t" + indentExtra;

		if (p.ScalarKind == CliScalarKind.Enum && p.EnumTypeFq is not null && !p.EnumMemberNames.IsDefaultOrEmpty)
		{
			EmitEnumParseFromString(sb, p, rawExpr, targetVar, ind, outVarKeyword, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
			return;
		}

		if (p.ScalarKind == CliScalarKind.FileInfo)
		{
			EmitFileInfoParseFromString(sb, p, rawExpr, targetVar, ind, outVarKeyword, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
			return;
		}

		if (p.ScalarKind == CliScalarKind.DirectoryInfo)
		{
			EmitDirectoryInfoParseFromString(sb, p, rawExpr, targetVar, ind, outVarKeyword, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
			return;
		}

		if (p.ScalarKind == CliScalarKind.Uri)
		{
			EmitUriParseFromString(sb, p, rawExpr, targetVar, ind, outVarKeyword, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
			return;
		}

		if (p.ScalarKind == CliScalarKind.CustomParser && p.ParserTypeFq is not null && p.CustomValueTypeFq is not null)
		{
			EmitCustomParserFromString(sb, p, rawExpr, targetVar, ind, outVarKeyword, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
			return;
		}

		if (p.Special == BoolSpecialKind.None && p.TypeName is "int?" or "long?" or "float?" or "double?" or "decimal?")
		{
			EmitNullableNumericParseFromString(sb, p, rawExpr, targetVar, ind, outVarKeyword, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
			return;
		}

		if (p.Special == BoolSpecialKind.None && p.TypeName is "DateTime?" or "DateTimeOffset?" or "TimeSpan?" or "DateOnly?")
		{
			EmitNullableTemporalParseFromString(sb, p, rawExpr, targetVar, ind, outVarKeyword, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
			return;
		}

		EmitPrimitiveScalarParseFromString(sb, p, rawExpr, targetVar, ind, outVarKeyword, failureExit, helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
	}



	private static void EmitEnumParseFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string ind, bool outVarKeyword, string failureExit, string? helpMethodName, string? flagHelpStdErrMethodName, string? parseFailureRunHint)
	{
		var e = Escape(p.CliLongName);
		string Out(string name) => outVarKeyword ? "out var " + name : "out " + name;
var evVar = "__ev_" + p.LocalVarName;
var evParsed = "__evp_" + p.LocalVarName;
sb.AppendLine($"{ind}var {evParsed} = false;");
sb.AppendLine($"{ind}{p.EnumTypeFq} {evVar} = default;");
sb.AppendLine($"{ind}switch (({rawExpr} ?? \"\").ToLowerInvariant())");
sb.AppendLine($"{ind}{{");
for (var i = 0; i < p.EnumMemberNames.Length; i++)
{
	var memberName = p.EnumMemberNames[i];
	var cliName = ResolveEnumMemberCliName(p.EnumMemberCliNames, i, memberName);
	sb.AppendLine($"{ind}\tcase \"{Escape(cliName.ToLowerInvariant())}\": {evVar} = {p.EnumTypeFq}.{memberName}; {evParsed} = true; break;");
}
sb.AppendLine($"{ind}}}");
sb.AppendLine($"{ind}if (!{evParsed})");
sb.AppendLine($"{ind}{{");
sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid value for --{e}: '{{{rawExpr}}}'.\");");
EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
sb.AppendLine($"{ind}\t{failureExit};");
sb.AppendLine($"{ind}}}");
if (outVarKeyword)
	sb.AppendLine($"{ind}var {targetVar} = {evVar};");
else
	sb.AppendLine($"{ind}{targetVar} = {evVar};");
return;
	}

	private static void EmitFileInfoParseFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string ind, bool outVarKeyword, string failureExit, string? helpMethodName, string? flagHelpStdErrMethodName, string? parseFailureRunHint)
	{
// Optional FileInfo? must omit new FileInfo when the flag was not provided (null), not pass null into the ctor (ArgumentNullException).
if (!p.IsRequired)
{
	var csharpNullableFi = GetCSharpCliType(p);
	var tmpFi = "__nullableFileInfo_" + Naming.SanitizeIdentifier(p.LocalVarName);
	sb.AppendLine($"{ind}{csharpNullableFi} {tmpFi} = null;");
	sb.AppendLine($"{ind}if (!string.IsNullOrWhiteSpace({rawExpr}))");
	sb.AppendLine($"{ind}{{");
	var innerFi = ind + "\t";
	string pathSrcOpt = $"{rawExpr}!";
	if (p.ExpandUserProfileBeforeBind)
	{
		var expandedOpt = "__path_" + Naming.SanitizeIdentifier(p.LocalVarName);
		sb.AppendLine($"{innerFi}var {expandedOpt} = global::Nullean.Argh.ArghPath.ExpandUserProfilePath({rawExpr}!);");
		pathSrcOpt = expandedOpt;
	}

	sb.AppendLine($"{innerFi}{tmpFi} = new global::System.IO.FileInfo({pathSrcOpt});");
	sb.AppendLine($"{ind}}}");
	if (outVarKeyword)
		sb.AppendLine($"{ind}var {targetVar} = {tmpFi};");
	else
		sb.AppendLine($"{ind}{targetVar} = {tmpFi};");
	return;
}

string pathSrc = $"{rawExpr}!";
if (p.ExpandUserProfileBeforeBind)
{
	var expandedName = "__path_" + Naming.SanitizeIdentifier(p.LocalVarName);
	sb.AppendLine($"{ind}var {expandedName} = global::Nullean.Argh.ArghPath.ExpandUserProfilePath({rawExpr}!);");
	pathSrc = expandedName;
}

if (outVarKeyword)
	sb.AppendLine($"{ind}var {targetVar} = new global::System.IO.FileInfo({pathSrc});");
else
	sb.AppendLine($"{ind}{targetVar} = new global::System.IO.FileInfo({pathSrc});");
return;
	}

	private static void EmitDirectoryInfoParseFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string ind, bool outVarKeyword, string failureExit, string? helpMethodName, string? flagHelpStdErrMethodName, string? parseFailureRunHint)
	{
// Optional DirectoryInfo? must omit new DirectoryInfo when the flag was not provided (null), not pass null into the ctor (ArgumentNullException).
if (!p.IsRequired)
{
	var csharpNullableDi = GetCSharpCliType(p);
	var tmpDi = "__nullableDirectoryInfo_" + Naming.SanitizeIdentifier(p.LocalVarName);
	sb.AppendLine($"{ind}{csharpNullableDi} {tmpDi} = null;");
	sb.AppendLine($"{ind}if (!string.IsNullOrWhiteSpace({rawExpr}))");
	sb.AppendLine($"{ind}{{");
	var innerDi = ind + "\t";
	string pathSrcDirOpt = $"{rawExpr}!";
	if (p.ExpandUserProfileBeforeBind)
	{
		var expandedOptDir = "__dir_" + Naming.SanitizeIdentifier(p.LocalVarName);
		sb.AppendLine($"{innerDi}var {expandedOptDir} = global::Nullean.Argh.ArghPath.ExpandUserProfilePath({rawExpr}!);");
		pathSrcDirOpt = expandedOptDir;
	}

	sb.AppendLine($"{innerDi}{tmpDi} = new global::System.IO.DirectoryInfo({pathSrcDirOpt});");
	sb.AppendLine($"{ind}}}");
	if (outVarKeyword)
		sb.AppendLine($"{ind}var {targetVar} = {tmpDi};");
	else
		sb.AppendLine($"{ind}{targetVar} = {tmpDi};");
	return;
}

string pathSrcDir = $"{rawExpr}!";
if (p.ExpandUserProfileBeforeBind)
{
	var expandedDir = "__dir_" + Naming.SanitizeIdentifier(p.LocalVarName);
	sb.AppendLine($"{ind}var {expandedDir} = global::Nullean.Argh.ArghPath.ExpandUserProfilePath({rawExpr}!);");
	pathSrcDir = expandedDir;
}

if (outVarKeyword)
	sb.AppendLine($"{ind}var {targetVar} = new global::System.IO.DirectoryInfo({pathSrcDir});");
else
	sb.AppendLine($"{ind}{targetVar} = new global::System.IO.DirectoryInfo({pathSrcDir});");
return;
	}

	private static void EmitUriParseFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string ind, bool outVarKeyword, string failureExit, string? helpMethodName, string? flagHelpStdErrMethodName, string? parseFailureRunHint)
	{
		var e = Escape(p.CliLongName);
// Optional Uri? must treat omitted flags as null (raw text is null), not run Uri.TryCreate on null/whitespace.
if (!p.IsRequired)
{
	var csharpNullableUri = GetCSharpCliType(p);
	var tmpUri = "__nullableUriParsed_" + Naming.SanitizeIdentifier(p.LocalVarName);
	sb.AppendLine($"{ind}{csharpNullableUri} {tmpUri} = null;");
	sb.AppendLine($"{ind}if (!string.IsNullOrWhiteSpace({rawExpr}))");
	sb.AppendLine($"{ind}{{");
	sb.AppendLine(
		$"{ind}\tif (!global::System.Uri.TryCreate({rawExpr}, global::System.UriKind.RelativeOrAbsolute, out var __uri))");
	sb.AppendLine($"{ind}\t{{");
	sb.AppendLine($"{ind}\t\tConsole.Error.WriteLine($\"Error: invalid URI for --{e}: '{{{rawExpr}}}'.\");");
	EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
	sb.AppendLine($"{ind}\t\t{failureExit};");
	sb.AppendLine($"{ind}\t}}");
	sb.AppendLine($"{ind}\t{tmpUri} = __uri;");
	sb.AppendLine($"{ind}}}");
	if (outVarKeyword)
		sb.AppendLine($"{ind}var {targetVar} = {tmpUri};");
	else
		sb.AppendLine($"{ind}{targetVar} = {tmpUri};");
	return;
}

sb.AppendLine($"{ind}if (!global::System.Uri.TryCreate({rawExpr}, global::System.UriKind.RelativeOrAbsolute, out var __uri))");
sb.AppendLine($"{ind}{{");
sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid URI for --{e}: '{{{rawExpr}}}'.\");");
EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
sb.AppendLine($"{ind}\t{failureExit};");
sb.AppendLine($"{ind}}}");
if (outVarKeyword)
	sb.AppendLine($"{ind}var {targetVar} = __uri;");
else
	sb.AppendLine($"{ind}{targetVar} = __uri;");
return;
	}

	private static void EmitCustomParserFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string ind, bool outVarKeyword, string failureExit, string? helpMethodName, string? flagHelpStdErrMethodName, string? parseFailureRunHint)
	{
		var e = Escape(p.CliLongName);
sb.AppendLine($"{ind}var __parser = new {p.ParserTypeFq}();");
sb.AppendLine($"{ind}if (!__parser.TryParse({rawExpr}!, out var __pv))");
sb.AppendLine($"{ind}{{");
sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid value for --{e}.\");");
EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
sb.AppendLine($"{ind}\t{failureExit};");
sb.AppendLine($"{ind}}}");
if (outVarKeyword)
	sb.AppendLine($"{ind}var {targetVar} = __pv;");
else
	sb.AppendLine($"{ind}{targetVar} = __pv;");
return;
	}

	private static void EmitPrimitiveScalarParseFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string ind, bool outVarKeyword, string failureExit, string? helpMethodName, string? flagHelpStdErrMethodName, string? parseFailureRunHint)
	{
		var e = Escape(p.CliLongName);
		string Out(string name) => outVarKeyword ? "out var " + name : "out " + name;
switch (p.Special)
{
	case BoolSpecialKind.None when p.TypeName == "string":
	{
		if (outVarKeyword)
			sb.AppendLine($"{ind}var {targetVar} = {rawExpr};");
		else
		{
			var nonNull = p.IsRequired ? "!" : "";
			sb.AppendLine($"{ind}{targetVar} = {rawExpr}{nonNull};");
		}

		break;
	}
	case BoolSpecialKind.None when p.TypeName == "int":
		sb.AppendLine(
			$"{ind}if (!int.TryParse({rawExpr}, NumberStyles.Integer, CultureInfo.InvariantCulture, {Out(targetVar)}))");
		sb.AppendLine($"{ind}{{");
		sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid int for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t{failureExit};");
		sb.AppendLine($"{ind}}}");
		break;
	case BoolSpecialKind.None when p.TypeName == "long":
		sb.AppendLine(
			$"{ind}if (!long.TryParse({rawExpr}, NumberStyles.Integer, CultureInfo.InvariantCulture, {Out(targetVar)}))");
		sb.AppendLine($"{ind}{{");
		sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid long for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t{failureExit};");
		sb.AppendLine($"{ind}}}");
		break;
	case BoolSpecialKind.None when p.TypeName == "float":
		sb.AppendLine(
			$"{ind}if (!float.TryParse({rawExpr}, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, {Out(targetVar)}))");
		sb.AppendLine($"{ind}{{");
		sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid float for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t{failureExit};");
		sb.AppendLine($"{ind}}}");
		break;
	case BoolSpecialKind.None when p.TypeName == "double":
		sb.AppendLine(
			$"{ind}if (!double.TryParse({rawExpr}, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, {Out(targetVar)}))");
		sb.AppendLine($"{ind}{{");
		sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid double for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t{failureExit};");
		sb.AppendLine($"{ind}}}");
		break;
	case BoolSpecialKind.None when p.TypeName == "decimal":
		sb.AppendLine(
			$"{ind}if (!decimal.TryParse({rawExpr}, NumberStyles.Number, CultureInfo.InvariantCulture, {Out(targetVar)}))");
		sb.AppendLine($"{ind}{{");
		sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid decimal for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t{failureExit};");
		sb.AppendLine($"{ind}}}");
		break;
	case BoolSpecialKind.None when p.TypeName == "DateTime":
	{
		var tmp = "__dt_" + p.LocalVarName;
		sb.AppendLine(
			$"{ind}if (!global::System.DateTime.TryParse({rawExpr}, CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind | global::System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var {tmp}))");
		sb.AppendLine($"{ind}{{");
		sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid DateTime for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t{failureExit};");
		sb.AppendLine($"{ind}}}");
		if (outVarKeyword)
			sb.AppendLine($"{ind}var {targetVar} = {tmp};");
		else
			sb.AppendLine($"{ind}{targetVar} = {tmp};");
		break;
	}
	case BoolSpecialKind.None when p.TypeName == "DateTimeOffset":
	{
		var tmp = "__dto_" + p.LocalVarName;
		sb.AppendLine(
			$"{ind}if (!global::System.DateTimeOffset.TryParse({rawExpr}, CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind | global::System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var {tmp}))");
		sb.AppendLine($"{ind}{{");
		sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid DateTimeOffset for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t{failureExit};");
		sb.AppendLine($"{ind}}}");
		if (outVarKeyword)
			sb.AppendLine($"{ind}var {targetVar} = {tmp};");
		else
			sb.AppendLine($"{ind}{targetVar} = {tmp};");
		break;
	}
	case BoolSpecialKind.None when p.TypeName == "TimeSpan":
	{
		var tmp = "__ts_" + p.LocalVarName;
		sb.AppendLine($"{ind}if (!global::Nullean.Argh.ArghTimeSpan.TryParse({rawExpr}, out var {tmp}))");
		sb.AppendLine($"{ind}{{");
		sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid TimeSpan for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t{failureExit};");
		sb.AppendLine($"{ind}}}");
		if (outVarKeyword)
			sb.AppendLine($"{ind}var {targetVar} = {tmp};");
		else
			sb.AppendLine($"{ind}{targetVar} = {tmp};");
		break;
	}
	case BoolSpecialKind.None when p.TypeName == "DateOnly":
	{
		var tmp = "__do_" + p.LocalVarName;
		sb.AppendLine(
			$"{ind}if (!global::System.DateOnly.TryParse({rawExpr}, CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.None, out var {tmp}))");
		sb.AppendLine($"{ind}{{");
		sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid DateOnly for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t{failureExit};");
		sb.AppendLine($"{ind}}}");
		if (outVarKeyword)
			sb.AppendLine($"{ind}var {targetVar} = {tmp};");
		else
			sb.AppendLine($"{ind}{targetVar} = {tmp};");
		break;
	}
	case BoolSpecialKind.None when p.TypeName == "bool":
		if (outVarKeyword)
			sb.AppendLine(
				$"{ind}var {targetVar} = bool.TryParse({rawExpr}, out var tmpBool) ? tmpBool : true;");
		else
			sb.AppendLine($"{ind}{targetVar} = bool.TryParse({rawExpr}, out var tmpBool) ? tmpBool : true;");
		break;
	default:
		if (outVarKeyword)
			sb.AppendLine($"{ind}var {targetVar} = {rawExpr};");
		else
			sb.AppendLine($"{ind}{targetVar} = {rawExpr}; // fallback");
		break;
}
	}

	private static void EmitNullableNumericParseFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar,
		string ind, bool outVarKeyword, string failureExit, string? helpMethodName, string? flagHelpStdErrMethodName = null, string? parseFailureRunHint = null)
	{
		var e = Escape(p.CliLongName);
		var tmpVar = "__nullableNumericParsed_" + p.LocalVarName;
		var parsedOut = "__nv_" + p.LocalVarName;
		var csharpNullable = GetCSharpCliType(p);

		sb.AppendLine($"{ind}{csharpNullable} {tmpVar} = null;");
		sb.AppendLine($"{ind}if ({rawExpr} is not null)");
		sb.AppendLine($"{ind}{{");

		switch (p.TypeName)
		{
			case "int?":
				sb.AppendLine(
					$"{ind}\tif (!int.TryParse({rawExpr}, NumberStyles.Integer, CultureInfo.InvariantCulture, out var {parsedOut}))");
				break;
			case "long?":
				sb.AppendLine(
					$"{ind}\tif (!long.TryParse({rawExpr}, NumberStyles.Integer, CultureInfo.InvariantCulture, out var {parsedOut}))");
				break;
			case "float?":
				sb.AppendLine(
					$"{ind}\tif (!float.TryParse({rawExpr}, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var {parsedOut}))");
				break;
			case "double?":
				sb.AppendLine(
					$"{ind}\tif (!double.TryParse({rawExpr}, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var {parsedOut}))");
				break;
			case "decimal?":
				sb.AppendLine(
					$"{ind}\tif (!decimal.TryParse({rawExpr}, NumberStyles.Number, CultureInfo.InvariantCulture, out var {parsedOut}))");
				break;
			default:
				throw new InvalidOperationException($"Unexpected nullable numeric type '{p.TypeName}'.");
		}

		sb.AppendLine($"{ind}\t{{");
		sb.AppendLine($"{ind}\t\tConsole.Error.WriteLine($\"Error: invalid {p.TypeName.TrimEnd('?')} for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t\t{failureExit};");
		sb.AppendLine($"{ind}\t}}");
		sb.AppendLine($"{ind}\t{tmpVar} = {parsedOut};");
		sb.AppendLine($"{ind}}}");

		if (outVarKeyword)
			sb.AppendLine($"{ind}var {targetVar} = {tmpVar};");
		else
			sb.AppendLine($"{ind}{targetVar} = {tmpVar};");
	}

	private static void EmitNullableTemporalParseFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar,
		string ind, bool outVarKeyword, string failureExit, string? helpMethodName, string? flagHelpStdErrMethodName = null, string? parseFailureRunHint = null)
	{
		var e = Escape(p.CliLongName);
		var tmpVar = "__nullableTemporalParsed_" + p.LocalVarName;
		var parsedOut = "__nt_" + p.LocalVarName;
		var csharpNullable = GetCSharpCliType(p);

		sb.AppendLine($"{ind}{csharpNullable} {tmpVar} = null;");
		sb.AppendLine($"{ind}if ({rawExpr} is not null)");
		sb.AppendLine($"{ind}{{");

		switch (p.TypeName)
		{
			case "DateTime?":
				sb.AppendLine(
					$"{ind}\tif (!global::System.DateTime.TryParse({rawExpr}, CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind | global::System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var {parsedOut}))");
				break;
			case "DateTimeOffset?":
				sb.AppendLine(
					$"{ind}\tif (!global::System.DateTimeOffset.TryParse({rawExpr}, CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind | global::System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var {parsedOut}))");
				break;
			case "TimeSpan?":
				sb.AppendLine($"{ind}\tif (!global::Nullean.Argh.ArghTimeSpan.TryParse({rawExpr}, out var {parsedOut}))");
				break;
			case "DateOnly?":
				sb.AppendLine(
					$"{ind}\tif (!global::System.DateOnly.TryParse({rawExpr}, CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.None, out var {parsedOut}))");
				break;
			default:
				throw new InvalidOperationException($"Unexpected nullable temporal type '{p.TypeName}'.");
		}

		sb.AppendLine($"{ind}\t{{");
		sb.AppendLine($"{ind}\t\tConsole.Error.WriteLine($\"Error: invalid {p.TypeName.TrimEnd('?')} for --{e}: '{{{rawExpr}}}'.\");");
		EmitAfterCliParseErrorHelp(sb, p, $"{ind}\t\t", helpMethodName, flagHelpStdErrMethodName, parseFailureRunHint);
		sb.AppendLine($"{ind}\t\t{failureExit};");
		sb.AppendLine($"{ind}\t}}");
		sb.AppendLine($"{ind}\t{tmpVar} = {parsedOut};");
		sb.AppendLine($"{ind}}}");

		if (outVarKeyword)
			sb.AppendLine($"{ind}var {targetVar} = {tmpVar};");
		else
			sb.AppendLine($"{ind}{targetVar} = {tmpVar};");
	}

	private static void EmitInvocation(
		StringBuilder sb,
		CommandModel cmd,
		string ctExpr = "ct",
		string? commandContextVar = null,
		string lineIndent = "\t\t\t",
		ImmutableArray<(string TypeFq, string TypeMetadataName, ImmutableArray<string> AllBaseTypeMetadataNames, string StaticFieldName, string LocalVarName, ImmutableArray<ParameterModel> FlatMembers, ImmutableArray<string>? BestCtorParamOrder)> injectedOptions = default)
	{
		// Lambda commands: invoke through ArghApp.GetRegisteredLambda with a cast
		if (cmd.IsLambda && !string.IsNullOrEmpty(cmd.LambdaStorageKey))
		{
			EmitLambdaInvocation(sb, cmd, ctExpr, commandContextVar, lineIndent);
			return;
		}

		var args = new List<string>();
		if (cmd.HandlerParamTypes.IsDefaultOrEmpty)
		{
			foreach (var p in cmd.Parameters)
			{
				if (p.Kind == ParameterKind.Injected)
					args.Add(ctExpr);
				else if (p.Kind != ParameterKind.OptionsInjected)
					// OptionsInjected entries are flag-recognition markers added by FixOptionsParamsInCommands;
					// they are not method arguments — the options instance is passed as a reconstructed local.
					args.Add(p.LocalVarName);
			}
		}
		else
		{
			foreach (var mp in cmd.HandlerParamTypes)
			{
				if (mp.IsInjectedParam)
				{
					args.Add(ctExpr);
					continue;
				}

				if (mp.IsAsParameters)
				{
					args.Add(AsParametersConstructedVarName(mp.Name));
					continue;
				}

				// Options-type parameters are injected as locally-reconstructed instances that merge
				// command-level flags (post-command) with pre-parsed static values (pre-command).
				if (!injectedOptions.IsDefaultOrEmpty)
				{
					string? localVar = null;
					foreach (var o in injectedOptions)
						if (o.TypeMetadataName == mp.TypeMetadataName) { localVar = o.LocalVarName; break; }
					if (localVar is null)
						for (var _i = injectedOptions.Length - 1; _i >= 0; _i--)
							if (injectedOptions[_i].AllBaseTypeMetadataNames.Contains(mp.TypeMetadataName)) { localVar = injectedOptions[_i].LocalVarName; break; }

					if (localVar is not null)
					{
						args.Add(localVar);
						continue;
					}
				}

				foreach (var p in cmd.Parameters)
				{
					if (p.AsParametersOwnerParamName is not null)
						continue;
					if (p.SymbolName != mp.Name)
						continue;
					args.Add(p.LocalVarName);
					break;
				}
			}
		}

		var argList = string.Join(", ", args);
		var call = cmd.RequiresInstance
			? $"__cmdHandler.{cmd.MethodName}({argList})"
			: $"{cmd.ContainingTypeFq}.{cmd.MethodName}({argList})";

		var ret0 = commandContextVar is null
			? $"{lineIndent}return 0;"
			: $"{lineIndent}{commandContextVar}.ExitCode = 0;\n{lineIndent}return;";

		var retFq = cmd.ReturnTypeFq;
		// Empty string means no return type info (shouldn't happen for method handlers).
		// Note: retFq comes from SymbolDisplayFormat.FullyQualifiedFormat, which renders special
		// types using their C# keyword ("void", "int") rather than "global::System.Void"/"global::System.Int32" —
		// keep these checks in that form (see also the "int" checks inside Task<int>/ValueTask<int> below).
		if (retFq == "" || retFq == "void")
		{
			sb.AppendLine($"{lineIndent}{call};");
			sb.AppendLine(ret0);
			return;
		}

		if (retFq == "int")
		{
			if (commandContextVar is null)
				sb.AppendLine($"{lineIndent}return {call};");
			else
			{
				sb.AppendLine($"{lineIndent}{commandContextVar}.ExitCode = {call};");
				sb.AppendLine($"{lineIndent}return;");
			}

			return;
		}

		if (retFq == "global::System.Threading.Tasks.Task")
		{
			sb.AppendLine($"{lineIndent}await {call}.ConfigureAwait(false);");
			sb.AppendLine(ret0);
			return;
		}

		if (retFq == "global::System.Threading.Tasks.Task<int>")
		{
			if (commandContextVar is null)
				sb.AppendLine($"{lineIndent}return await {call}.ConfigureAwait(false);");
			else
			{
				sb.AppendLine($"{lineIndent}{commandContextVar}.ExitCode = await {call}.ConfigureAwait(false);");
				sb.AppendLine($"{lineIndent}return;");
			}
			return;
		}

		if (retFq.StartsWith("global::System.Threading.Tasks.Task<", StringComparison.Ordinal))
		{
			sb.AppendLine($"{lineIndent}await {call}.ConfigureAwait(false);");
			sb.AppendLine(ret0);
			return;
		}

		if (retFq == "global::System.Threading.Tasks.ValueTask")
		{
			sb.AppendLine($"{lineIndent}await {call}.ConfigureAwait(false);");
			sb.AppendLine(ret0);
			return;
		}

		if (retFq == "global::System.Threading.Tasks.ValueTask<int>")
		{
			if (commandContextVar is null)
				sb.AppendLine($"{lineIndent}return await {call}.ConfigureAwait(false);");
			else
			{
				sb.AppendLine($"{lineIndent}{commandContextVar}.ExitCode = await {call}.ConfigureAwait(false);");
				sb.AppendLine($"{lineIndent}return;");
			}
			return;
		}

		if (retFq.StartsWith("global::System.Threading.Tasks.ValueTask<", StringComparison.Ordinal))
		{
			sb.AppendLine($"{lineIndent}await {call}.ConfigureAwait(false);");
			sb.AppendLine(ret0);
			return;
		}

		sb.AppendLine($"{lineIndent}{call};");
		sb.AppendLine(ret0);
	}

	private static void EmitLambdaInvocation(
		StringBuilder sb,
		CommandModel cmd,
		string ctExpr,
		string? commandContextVar,
		string lineIndent)
	{
		var lambdaArgs = new List<string>();
		foreach (var p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Injected)
				lambdaArgs.Add(ctExpr);
			else
				lambdaArgs.Add(p.LocalVarName);
		}
		var lambdaArgList = string.Join(", ", lambdaArgs);
		var castType = string.IsNullOrEmpty(cmd.LambdaDelegateFq) || cmd.LambdaDelegateFq == "global::System.Delegate"
			? "global::System.Delegate"
			: cmd.LambdaDelegateFq;

		var lambdaRet0 = commandContextVar is null
			? $"{lineIndent}return 0;"
			: $"{lineIndent}{commandContextVar}.ExitCode = 0;\n{lineIndent}return;";

		var lambdaRetFq = cmd.ReturnTypeFq;
		var lambdaIsTaskOfInt = lambdaRetFq == "global::System.Threading.Tasks.Task<int>"
			|| lambdaRetFq == "global::System.Threading.Tasks.ValueTask<int>";

		if (castType == "global::System.Delegate")
		{
			// Fallback: use DynamicInvoke
			sb.AppendLine($"{lineIndent}var __lambdaDelegate = ArghApp.GetRegisteredLambda(\"{Escape(cmd.LambdaStorageKey)}\");");
			sb.AppendLine($"{lineIndent}__lambdaDelegate?.DynamicInvoke({lambdaArgList});");
			sb.AppendLine(lambdaRet0);
		}
		else
		{
			sb.AppendLine($"{lineIndent}var __lambdaDelegate = (({castType})ArghApp.GetRegisteredLambda(\"{Escape(cmd.LambdaStorageKey)}\")!);");
			if (lambdaRetFq == "global::System.Threading.Tasks.Task" ||
			    (lambdaRetFq.StartsWith("global::System.Threading.Tasks.Task<", System.StringComparison.Ordinal) && !lambdaIsTaskOfInt))
			{
				sb.AppendLine($"{lineIndent}await __lambdaDelegate({lambdaArgList}).ConfigureAwait(false);");
				sb.AppendLine(lambdaRet0);
			}
			else if (lambdaIsTaskOfInt)
			{
				if (commandContextVar is null)
					sb.AppendLine($"{lineIndent}return await __lambdaDelegate({lambdaArgList}).ConfigureAwait(false);");
				else
				{
					sb.AppendLine($"{lineIndent}{commandContextVar}.ExitCode = await __lambdaDelegate({lambdaArgList}).ConfigureAwait(false);");
					sb.AppendLine($"{lineIndent}return;");
				}
			}
			else if (lambdaRetFq == "int")
			{
				if (commandContextVar is null)
					sb.AppendLine($"{lineIndent}return __lambdaDelegate({lambdaArgList});");
				else
				{
					sb.AppendLine($"{lineIndent}{commandContextVar}.ExitCode = __lambdaDelegate({lambdaArgList});");
					sb.AppendLine($"{lineIndent}return;");
				}
			}
			else
			{
				sb.AppendLine($"{lineIndent}__lambdaDelegate({lambdaArgList});");
				sb.AppendLine(lambdaRet0);
			}
		}
	}

	private static IEnumerable<ParameterModel> EnumerateFlagMembers(OptionsTypeModel? model)
	{
		if (model is null)
			yield break;

		foreach (var p in model.Members)
		{
			if (p.Kind == ParameterKind.Flag)
				yield return p;
		}
	}

	private static void AddCliKeys(IEnumerable<ParameterModel> flags, HashSet<string> keys)
	{
		foreach (var p in flags)
		{
			keys.Add(p.CliLongName);
			foreach (var a in p.Aliases)
			{
				if (!string.IsNullOrEmpty(a))
					keys.Add(a);
			}
		}
	}

	private static List<(string Segment, OptionsTypeModel Model)> GetCommandNamespaceOptionChain(AppEmitModel app, ImmutableArray<string> routePrefix)
	{
		var list = new List<(string, OptionsTypeModel)>();
		var current = app.Root;
		foreach (var seg in routePrefix)
		{
			RegistryNode.NamedCommandNamespaceChild? found = null;
			foreach (var c in current.Children)
			{
				if (string.Equals(c.Segment, seg, StringComparison.OrdinalIgnoreCase))
				{
					found = c;
					break;
				}
			}

			if (found is null)
				break;

			current = found.Node;
			if (current.CommandNamespaceOptionsModel is { Members: { Length: > 0 } } gom)
				list.Add((seg, gom));
		}

		return list;
	}

	private static bool CommandFlagMatchesScopedKeys(ParameterModel p, HashSet<string> scopedKeys)
	{
		if (scopedKeys.Contains(p.CliLongName))
			return true;

		foreach (var a in p.Aliases)
		{
			if (!string.IsNullOrEmpty(a) && scopedKeys.Contains(a))
				return true;
		}

		return false;
	}


	/// <summary>
	/// Diagnoses misuse of the filesystem-path attribute family. <paramref name="filesystemScalarKind"/> lets
	/// collection call sites pass the *element* kind (e.g. <c>FileInfo</c> for <c>List&lt;FileInfo&gt;</c>) so these
	/// attributes are correctly recognized on collections of FileInfo/DirectoryInfo, not just scalars.
	/// </summary>
	private static void ReportFilesystemPathAttributeIssues(
		ISymbol host,
		CliScalarKind scalarKind,
		string declaredName,
		DiagnosticAccumulator? acc,
		Location? fallbackLocation,
		CliScalarKind? filesystemScalarKind = null)
	{
		Location loc = host.Locations.FirstOrDefault() ?? fallbackLocation ?? Location.None;
		var fsKind = filesystemScalarKind ?? scalarKind;

		var hasExisting = false;
		var hasNonExisting = false;
		var hasExpandProfile = false;
		var hasRejectSymlinks = false;
		var hasFileExtensions = false;

		foreach (var attr in host.GetAttributes())
		{
			var fqn = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
			switch (fqn)
			{
				case "global::Nullean.Argh.ExistingAttribute":
					hasExisting = true;
					break;
				case "global::Nullean.Argh.NonExistingAttribute":
					hasNonExisting = true;
					break;
				case "global::Nullean.Argh.ExpandUserProfileAttribute":
					hasExpandProfile = true;
					break;
				case "global::Nullean.Argh.RejectSymbolicLinksAttribute":
					hasRejectSymlinks = true;
					break;
				case "global::System.ComponentModel.DataAnnotations.FileExtensionsAttribute":
					hasFileExtensions = true;
					break;
			}
		}

		if (hasExisting && hasNonExisting)
			acc?.Add(PathExistenceAttributesConflict, loc, declaredName);

		var isFileInfo = fsKind == CliScalarKind.FileInfo;
		var isDirInfo = fsKind == CliScalarKind.DirectoryInfo;
		var isFileOrDir = isFileInfo || isDirInfo;

		if (hasExisting && !isFileOrDir)
			acc?.Add(FilesystemPathAttributeTypeMismatch, loc, declaredName,
				"[Existing] only applies to FileInfo, FileInfo?, DirectoryInfo, DirectoryInfo?, or a collection of FileInfo/DirectoryInfo parameters and properties.");

		if (hasNonExisting && !isFileOrDir)
			acc?.Add(FilesystemPathAttributeTypeMismatch, loc, declaredName,
				"[NonExisting] only applies to FileInfo, FileInfo?, DirectoryInfo, DirectoryInfo?, or a collection of FileInfo/DirectoryInfo parameters and properties.");

		if (hasExpandProfile && !isFileOrDir)
			acc?.Add(FilesystemPathAttributeTypeMismatch, loc, declaredName,
				"[ExpandUserProfile] only applies to FileInfo, DirectoryInfo, or a collection of FileInfo/DirectoryInfo parameters and properties.");

		if (hasRejectSymlinks && !isFileOrDir)
			acc?.Add(FilesystemPathAttributeTypeMismatch, loc, declaredName,
				"[RejectSymbolicLinks] only applies to FileInfo, DirectoryInfo, or a collection of FileInfo/DirectoryInfo parameters and properties.");

		if (hasFileExtensions && !isFileInfo)
			acc?.Add(FilesystemPathAttributeTypeMismatch, loc, declaredName,
				"[FileExtensions] only applies to FileInfo, FileInfo?, or a collection of FileInfo parameters and properties.");
	}

	private static bool TryReadExpandUserProfileBeforeBind(ISymbol host, CliScalarKind scalarKind)
	{
		foreach (var attr in host.GetAttributes())
		{
			if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
			    "global::Nullean.Argh.ExpandUserProfileAttribute")
			{
				return scalarKind is CliScalarKind.FileInfo or CliScalarKind.DirectoryInfo;
			}
		}

		return false;
	}


	/// <summary>
	/// Reads DataAnnotations/Argh validation attributes off <paramref name="attributeHost"/>.
	/// </summary>
	/// <param name="filesystemScalarKind">
	/// Kind used to gate the filesystem-path family ([Existing], [NonExisting], [RejectSymbolicLinks], [FileExtensions]).
	/// For scalar parameters this equals <paramref name="scalarKind"/> (the default when null). For collection
	/// parameters (<c>List&lt;FileInfo&gt;</c>, <c>DirectoryInfo[]</c>, ...) callers pass the *element* kind here so
	/// these attributes are recognized per-item while <paramref name="scalarKind"/> stays <see cref="CliScalarKind.Collection"/>
	/// for the other (non filesystem-family) constraint decisions such as [Url] vs Uri-scheme.
	/// </param>
}
