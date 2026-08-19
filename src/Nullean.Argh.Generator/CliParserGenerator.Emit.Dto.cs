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
	private sealed record DtoBindingTarget(
		string TypeFq,
		ImmutableArray<ParameterModel> Members,
		bool IsOptionsDto,
		bool IsGeneric,
		bool IsPublic,
		ImmutableArray<string>? BestCtorParamOrder);

	private static ImmutableArray<DtoBindingTarget> CollectDtoBindingTargets(
		AppEmitModel app)
	{
		// Use string TypeFq as dedup key since we no longer have INamedTypeSymbol in the pipeline boundary.
		var map = new Dictionary<string, DtoBindingTarget>(StringComparer.Ordinal);

		if (app.GlobalOptionsModel is { } gom && gom.FlattenedMembers.Length > 0)
		{
			map[gom.TypeFq] = new DtoBindingTarget(
				gom.TypeFq,
				gom.FlattenedMembers,
				IsOptionsDto: true,
				IsGeneric: gom.IsGeneric,
				IsPublic: gom.IsPublic,
				gom.BestCtorParamOrder);
		}

		foreach ((var node, _) in EnumerateCommandNamespaceNodesWithPath(app.Root, ImmutableArray<string>.Empty))
		{
			if (node.CommandNamespaceOptionsModel is not { } nsModel)
				continue;
			if (nsModel.FlattenedMembers.Length == 0)
				continue;
			if (map.ContainsKey(nsModel.TypeFq))
				continue;
			map[nsModel.TypeFq] = new DtoBindingTarget(
				nsModel.TypeFq,
				nsModel.FlattenedMembers,
				IsOptionsDto: true,
				IsGeneric: nsModel.IsGeneric,
				IsPublic: nsModel.IsPublic,
				nsModel.BestCtorParamOrder);
		}

		foreach (var cmd in app.AllCommands)
		{
			if (cmd.HandlerParamTypes.IsDefaultOrEmpty)
				continue;

			foreach (var mp in cmd.HandlerParamTypes)
			{
				if (!mp.IsAsParameters || mp.AsParamTypeFq is not { } typeFq)
					continue;
				if (string.IsNullOrEmpty(typeFq) || map.ContainsKey(typeFq))
					continue;

				// Extract the already-flattened DTO members from the command's Parameters array
				// (these were computed by FlattenAsParametersType during analysis and include proper prefix/AsParametersMeta).
				var flat = cmd.Parameters
					.Where(p => p.AsParametersOwnerParamName == mp.Name)
					.ToImmutableArray();

				if (flat.Length > 0)
					map[typeFq] = new DtoBindingTarget(
						typeFq,
						flat,
						IsOptionsDto: false,
						IsGeneric: mp.AsParamIsGeneric,
						IsPublic: mp.AsParamIsPublic,
						mp.AsParamBestCtorParamOrder);
			}
		}

		return map.Values
			.OrderBy(t => t.TypeFq, StringComparer.Ordinal)
			.ToImmutableArray();
	}

	private static string OptionsStaticFieldName(INamedTypeSymbol type) =>
		"s_opts_" + DtoMethodSuffix(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

	private static string OptionsStaticFieldNameFq(string typeFq) =>
		"s_opts_" + DtoMethodSuffix(typeFq);

	/// <summary>Name of the per-command-runner local variable that holds the reconstructed options instance.</summary>
	private static string OptionsLocalVarName(INamedTypeSymbol type) =>
		"__opts_" + DtoMethodSuffix(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

	private static string OptionsLocalVarNameFq(string typeFq) =>
		"__opts_" + DtoMethodSuffix(typeFq);

	private static string DtoMethodSuffix(INamedTypeSymbol type) =>
		DtoMethodSuffix(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

	private static string DtoMethodSuffix(string typeFq)
	{
		var fq = typeFq;
		if (fq.StartsWith("global::", StringComparison.Ordinal))
			fq = fq.Substring(8);

		var sb = new StringBuilder();
		foreach (var c in fq)
		{
			if (char.IsLetterOrDigit(c))
				sb.Append(c);
			else
				sb.Append('_');
		}

		return sb.Length == 0 ? "Dto" : sb.ToString();
	}

	private static void EmitDtoBindingMethods(StringBuilder sb, ImmutableArray<DtoBindingTarget> targets)
	{
		foreach (var t in targets)
		{
			var suffix = DtoMethodSuffix(t.TypeFq);
			var lenientName = "TryParseDto_" + suffix;
			var strictName = "TryParseDtoExact_" + suffix;
			var syn = SyntheticOptionsCommand(t.Members, lenientName);

			EmitCommandRunner(
				sb,
				syn,
				ImmutableArray<GlobalMiddlewareRegistration>.Empty,
				emitDtoTryParse: true,
				dtoLenient: true,
				dtoMethodName: lenientName,
				dtoResultTypeFq: t.TypeFq,
				dtoOptionsTypeFq: t.IsOptionsDto ? t.TypeFq : null,
				dtoOptionsBestCtorParamOrder: t.IsOptionsDto ? t.BestCtorParamOrder : null);

			EmitCommandRunner(
				sb,
				syn,
				ImmutableArray<GlobalMiddlewareRegistration>.Empty,
				emitDtoTryParse: true,
				dtoLenient: false,
				dtoMethodName: strictName,
				dtoResultTypeFq: t.TypeFq,
				dtoOptionsTypeFq: t.IsOptionsDto ? t.TypeFq : null,
				dtoOptionsBestCtorParamOrder: t.IsOptionsDto ? t.BestCtorParamOrder : null);
		}
	}

	private static void EmitDtoTypeExtensions(
		SourceProductionContext context,
		ImmutableArray<DtoBindingTarget> targets,
		string arghGeneratedRootTypeName)
	{
		if (targets.IsEmpty)
			return;

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using System;");
		sb.AppendLine("using System.Diagnostics.CodeAnalysis;");
		sb.AppendLine();
		sb.AppendLine("namespace Nullean.Argh");
		sb.AppendLine("{");
		sb.AppendLine("\t/// <summary>Source-generated DTO parsers. Uses C# 14 extension members (static extensions on each DTO type) plus a <see cref=\"Type\"/>-based overload for generic dispatch.</summary>");
		sb.AppendLine("\tpublic static class ArghTypeBindingExtensions");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tpublic static bool TryParseArgh<T>(this Type type, string[] args, [NotNullWhen(true)] out T? value) where T : class");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tvalue = null;");
		sb.AppendLine("\t\t\tif (!ReferenceEquals(type, typeof(T)))");
		sb.AppendLine("\t\t\t\tthrow new ArgumentException(\"The receiver must be typeof(T).\", nameof(type));");
		foreach (var t in targets)
		{
			var fq = t.TypeFq;
			var method = "TryParseDto_" + DtoMethodSuffix(fq);
			sb.AppendLine($"\t\t\tif (typeof(T) == typeof({fq}))");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tvar ok = {arghGeneratedRootTypeName}.{method}(args, out var v);");
			sb.AppendLine("\t\t\t\tvalue = (T?)(object?)v;");
			sb.AppendLine("\t\t\t\treturn ok;");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine(
			"\t\t\tthrow new InvalidOperationException(\"No pregenerated Argh DTO parser for \" + typeof(T).FullName + \". Register the type as UseGlobalOptions/UseNamespaceOptions or use it with [AsParameters] on a command.\");");
		sb.AppendLine("\t\t}");
		sb.AppendLine();

		sb.AppendLine("\t\tpublic static bool TryParseArghExact<T>(this Type type, string[] args, [NotNullWhen(true)] out T? value) where T : class");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tvalue = null;");
		sb.AppendLine("\t\t\tif (!ReferenceEquals(type, typeof(T)))");
		sb.AppendLine("\t\t\t\tthrow new ArgumentException(\"The receiver must be typeof(T).\", nameof(type));");
		foreach (var t in targets)
		{
			var fq = t.TypeFq;
			var method = "TryParseDtoExact_" + DtoMethodSuffix(fq);
			sb.AppendLine($"\t\t\tif (typeof(T) == typeof({fq}))");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tvar ok = {arghGeneratedRootTypeName}.{method}(args, out var v);");
			sb.AppendLine("\t\t\t\tvalue = (T?)(object?)v;");
			sb.AppendLine("\t\t\t\treturn ok;");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine(
			"\t\t\tthrow new InvalidOperationException(\"No pregenerated Argh DTO parser for \" + typeof(T).FullName + \". Register the type as UseGlobalOptions/UseNamespaceOptions or use it with [AsParameters] on a command.\");");
		sb.AppendLine("\t\t}");
		sb.AppendLine();

		foreach (var t in targets)
		{
			if (t.IsGeneric)
				continue;

			var fq = t.TypeFq;
			var lenientMethod = "TryParseDto_" + DtoMethodSuffix(fq);
			var strictMethod = "TryParseDtoExact_" + DtoMethodSuffix(fq);
			var vis = t.IsPublic ? "public" : "internal";
			sb.AppendLine($"\t\textension({fq})");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\t{vis} static bool TryParseArgh(string[] args, [NotNullWhen(true)] out {fq}? value) =>");
			sb.AppendLine($"\t\t\t\tglobal::Nullean.Argh.{arghGeneratedRootTypeName}.{lenientMethod}(args, out value);");
			sb.AppendLine($"\t\t\t{vis} static bool TryParseArghExact(string[] args, [NotNullWhen(true)] out {fq}? value) =>");
			sb.AppendLine($"\t\t\t\tglobal::Nullean.Argh.{arghGeneratedRootTypeName}.{strictMethod}(args, out value);");
			sb.AppendLine("\t\t}");
			sb.AppendLine();
		}

		sb.AppendLine("\t}");
		sb.AppendLine("}");
		context.AddSource("ArghTypeBindingExtensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}



}
