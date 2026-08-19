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
	private static void ReportBoolNegationSwitchConflictsAcc(
		DiagnosticAccumulator acc,
		Location fallbackLocation,
		ImmutableArray<ParameterModel> parameters,
		IMethodSymbol method)
	{
		var locByParamName = new Dictionary<string, Location>(StringComparer.Ordinal);
		foreach (var sym in method.Parameters)
		{
			if (sym.Locations.Length == 0)
				continue;
			var loc = sym.Locations[0];
			if (loc.IsInSource)
				locByParamName[sym.Name] = loc;
		}

		foreach (var nullable in parameters)
		{
			if (nullable.Kind != ParameterKind.Flag || nullable.Special != BoolSpecialKind.NullableBool)
				continue;
			var negCli = "no-" + nullable.CliLongName;
			foreach (var plain in parameters)
			{
				if (plain.Kind != ParameterKind.Flag || plain.Special != BoolSpecialKind.Bool)
					continue;
				if (!string.Equals(plain.CliLongName, negCli, StringComparison.OrdinalIgnoreCase))
					continue;
				var loc = locByParamName.TryGetValue(plain.SymbolName, out var l) ? l : fallbackLocation;
				acc.Add(BoolFlagCollidesWithNullableNegation, loc, plain.SymbolName, plain.CliLongName);
			}
		}
	}




	/// <summary>DiagnosticAccumulator-based overload for Select-step analysis.</summary>
	private static void ReportDuplicateCliNamesAcc(DiagnosticAccumulator acc, Location location, ImmutableArray<ParameterModel> parameters)
	{
		var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var p in parameters)
		{
			if (p.Kind != ParameterKind.Flag) continue;
			void check(string name)
			{
				if (string.IsNullOrEmpty(name)) return;
				if (seen.TryGetValue(name, out var first))
				{
					if (!string.Equals(first, p.SymbolName, StringComparison.Ordinal))
						acc.Add(DuplicateCliNames, location, name);
				}
				else
					seen[name] = p.SymbolName;
			}
			check(p.CliLongName);
			foreach (var al in p.Aliases) check(al);
			if (p.Special == BoolSpecialKind.NullableBool) check("no-" + p.CliLongName);
		}
	}

	/// <summary>
	/// Each generated <c>TryApplyShortFlag</c> groups all flag-like params in one scope (global prefetch, namespace
	/// prefetch, or a single handler). Duplicate single-letter shortcuts would emit invalid duplicate <c>case</c> labels.
	/// </summary>
	private static void ValidateDuplicateShortOptionLetters(SourceProductionContext context, AppEmitModel app)
	{
		if (app.GlobalOptionsModel is { FlattenedMembers: var gm } && !gm.IsDefaultOrEmpty)
			ReportDuplicateShortsAmongMembers(context, Location.None, gm, "global options");

		static void walkNs(RegistryNode node, SourceProductionContext ctx)
		{
			if (node.CommandNamespaceOptionsModel is { FlattenedMembers: var nm } && !nm.IsDefaultOrEmpty)
			{
				var loc = node.CommandNamespaceOptionsLocation ?? Location.None;
				ReportDuplicateShortsAmongMembers(ctx, loc, nm, "namespace-scoped options");
			}

			foreach (var ch in node.Children)
				walkNs(ch.Node, ctx);
		}

		walkNs(app.Root, context);

		foreach (var cmd in app.AllCommands)
		{
			if (cmd.Parameters.IsDefaultOrEmpty)
				continue;
			var loc = cmd.HandlerSpanInfo.ToLocation();
			var scope =
				cmd.RoutePrefix.IsDefaultOrEmpty
					? $"command '{cmd.CommandName}'"
					: $"command '{string.Join(" ", cmd.RoutePrefix)} {cmd.CommandName}'";
			ReportDuplicateShortsAmongMembers(context, loc, cmd.Parameters, scope);
		}
	}

	private static void ReportDuplicateShortsAmongMembers(
		SourceProductionContext context,
		Location location,
		ImmutableArray<ParameterModel> members,
		string scopeDescription)
	{
		var byChar = new Dictionary<char, string>();
		foreach (var p in members)
		{
			if (!IsEmittedFlagLike(p.Kind))
				continue;
			if (p.ShortOpt is not char ch)
				continue;
			if (byChar.TryGetValue(ch, out var firstLong))
			{
				context.ReportDiagnostic(Diagnostic.Create(
					DuplicateShortOption,
					location,
					ch.ToString(),
					firstLong,
					p.CliLongName,
					scopeDescription));
			}
			else
			{
				byChar[ch] = p.CliLongName;
			}
		}
	}

	/// <summary>DiagnosticAccumulator-based overload for Select-step analysis.</summary>
	private static void ValidateExpandedParameterLayoutAcc(DiagnosticAccumulator acc, Location location, ImmutableArray<ParameterModel> expanded)
	{
		var seenFlag = false;
		foreach (var p in expanded)
		{
			if (p.Kind == ParameterKind.Injected) continue;
			if (p.Kind == ParameterKind.Flag) { seenFlag = true; continue; }
			// A variadic positional is allowed after flags — C# requires params to be last.
			if (p.Kind == ParameterKind.Positional && seenFlag && !p.IsVariadic)
			{
				acc.Add(ArgumentOrder, location);
				return;
			}
		}
	}

	private static void ValidateVariadicPositionalIsLastAcc(DiagnosticAccumulator acc, Location location, ImmutableArray<ParameterModel> parameters)
	{
		var sawVariadic = false;
		foreach (var p in parameters)
		{
			if (p.Kind != ParameterKind.Positional) continue;
			if (sawVariadic) { acc.Add(VariadicMustBeLastPositional, location); return; }
			if (p.IsVariadic) sawVariadic = true;
		}
	}

	/// <summary>DiagnosticAccumulator-based overload for Select-step analysis.</summary>
	private static ImmutableArray<ParameterModel> FlattenAsParametersTypeAcc(
		DiagnosticAccumulator acc,
		Location location,
		IParameterSymbol methodParam,
		INamedTypeSymbol type,
		string? prefix,
		Compilation? compilation,
		CSharpParseOptions parseOptions)
	{
		var pfx = string.IsNullOrWhiteSpace(prefix) ? "" : Naming.ToCliLongName(prefix!.Trim()) + "-";
		var owner = methodParam.Name;
		var typeFq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var primary = TryGetPrimaryConstructor(type);
		var ctorNames = new HashSet<string>(StringComparer.Ordinal);
		var list = new List<ParameterModel>();
		var order = 0;
		if (primary is not null)
		{
			foreach (var cp in primary.Parameters)
			{
				ctorNames.Add(cp.Name);
				list.Add(ParameterModel.FromAsParametersCtorParameter(owner, typeFq, type, cp, pfx, order++, compilation, parseOptions,
					acc,
					location));
			}
		}
		var chain = new List<INamedTypeSymbol>();
		for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
			chain.Add(t);
		var seenPropNames = new HashSet<string>(StringComparer.Ordinal);
		for (var i = chain.Count - 1; i >= 0; i--)
		{
			var tt = chain[i];
			foreach (var member in tt.GetMembers())
			{
				if (member is not IPropertySymbol prop) continue;
				if (prop.DeclaredAccessibility != Accessibility.Public || prop.IsStatic || prop.IsIndexer) continue;
				if (!IsSettableForAsParameters(prop)) continue;
				if (ctorNames.Contains(prop.Name)) continue;
				if (!seenPropNames.Add(prop.Name)) continue;
				list.Add(ParameterModel.FromAsParametersInitProperty(methodParamName: owner, typeFq, prop, pfx, order++, compilation, parseOptions,
					acc,
					location));
			}
		}
		if (list.Count == 0)
			acc.Add(AsParametersEmptyType, location, type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat));
		return list.ToImmutableArray();
	}


	private static string? TryGetStringLiteral(ExpressionSyntax expr) =>
		expr switch
		{
			LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } lit => lit.Token.ValueText,
			_ => null
		};

	/// <summary>Unique per compilation assembly so generated CLI types do not collide across referenced assemblies (e.g. CS0436 with InternalsVisibleTo).</summary>
	private static ImmutableArray<ValidationConstraint> ReadValidationConstraints(ISymbol attributeHost, CliScalarKind scalarKind,
		string primitiveTypeName, bool isCollection = false, CliScalarKind? filesystemScalarKind = null)
	{
		var fsKind = filesystemScalarKind ?? scalarKind;
		var builder = ImmutableArray.CreateBuilder<ValidationConstraint>();
		foreach (var attr in attributeHost.GetAttributes())
		{
			var fqn = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
			switch (fqn)
			{
				case "global::System.ComponentModel.DataAnnotations.RangeAttribute":
					if (attr.ConstructorArguments.Length >= 2)
						builder.Add(new RangeConstraint(attr.ConstructorArguments[0].ToCSharpString(), attr.ConstructorArguments[1].ToCSharpString()));
					break;
				case "global::Nullean.Argh.TimeSpanRangeAttribute":
					if (primitiveTypeName is "TimeSpan" or "TimeSpan?" &&
					    attr.ConstructorArguments.Length >= 2)
						builder.Add(new TimeSpanRangeConstraint(
							attr.ConstructorArguments[0].ToCSharpString(),
							attr.ConstructorArguments[1].ToCSharpString()));
					break;
				case "global::System.ComponentModel.DataAnnotations.StringLengthAttribute":
					if (attr.ConstructorArguments.Length >= 1)
					{
						var max = (int?)(int?)attr.ConstructorArguments[0].Value;
						int? min = null;
						foreach (var n in attr.NamedArguments)
							if (n.Key == "MinimumLength") min = (int?)n.Value.Value;
						if (isCollection)
							builder.Add(new CollectionCountConstraint(min, max));
						else
							builder.Add(new StringLengthConstraint(min, max));
					}
					break;
				case "global::System.ComponentModel.DataAnnotations.MinLengthAttribute":
					if (attr.ConstructorArguments.Length >= 1)
					{
						if (isCollection)
							builder.Add(new CollectionCountConstraint((int?)attr.ConstructorArguments[0].Value, null));
						else
							builder.Add(new StringLengthConstraint((int?)attr.ConstructorArguments[0].Value, null));
					}
					break;
				case "global::System.ComponentModel.DataAnnotations.MaxLengthAttribute":
					if (attr.ConstructorArguments.Length >= 1)
					{
						if (isCollection)
							builder.Add(new CollectionCountConstraint(null, (int?)attr.ConstructorArguments[0].Value));
						else
							builder.Add(new StringLengthConstraint(null, (int?)attr.ConstructorArguments[0].Value));
					}
					break;
				case "global::System.ComponentModel.DataAnnotations.LengthAttribute":
					if (attr.ConstructorArguments.Length >= 2)
					{
						if (isCollection)
							builder.Add(new CollectionCountConstraint((int?)attr.ConstructorArguments[0].Value, (int?)attr.ConstructorArguments[1].Value));
						else
							builder.Add(new StringLengthConstraint((int?)attr.ConstructorArguments[0].Value, (int?)attr.ConstructorArguments[1].Value));
					}
					break;
				case "global::System.ComponentModel.DataAnnotations.RegularExpressionAttribute":
					if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is string pat)
						builder.Add(new RegexConstraint(pat));
					break;
				case "global::System.ComponentModel.DataAnnotations.AllowedValuesAttribute":
					if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Kind == TypedConstantKind.Array)
					{
						var vals = attr.ConstructorArguments[0].Values.Select(v => v.ToCSharpString()).ToImmutableArray();
						if (!vals.IsEmpty) builder.Add(new AllowedValuesConstraint(vals));
					}
					break;
				case "global::System.ComponentModel.DataAnnotations.DeniedValuesAttribute":
					if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Kind == TypedConstantKind.Array)
					{
						var vals = attr.ConstructorArguments[0].Values.Select(v => v.ToCSharpString()).ToImmutableArray();
						if (!vals.IsEmpty) builder.Add(new DeniedValuesConstraint(vals));
					}
					break;
				case "global::System.ComponentModel.DataAnnotations.EmailAddressAttribute":
					builder.Add(new EmailConstraint());
					break;
				case "global::System.ComponentModel.DataAnnotations.UrlAttribute":
					if (scalarKind == CliScalarKind.Uri)
						builder.Add(new UriSchemeConstraint(ImmutableArray.Create("http", "https")));
					else
						builder.Add(new UrlConstraint());
					break;
				case "global::System.ComponentModel.DataAnnotations.FileExtensionsAttribute":
				{
					if (fsKind != CliScalarKind.FileInfo)
						break;
					string? extsStr = null;
					foreach (var n in attr.NamedArguments)
						if (n.Key == "Extensions") extsStr = n.Value.Value as string;
					extsStr ??= "png,jpg,jpeg,gif";
					var exts = extsStr.Split(',').Select(e => e.Trim().TrimStart('.')).ToImmutableArray();
					builder.Add(new FileExtensionsConstraint(exts));
					break;
				}
				case "global::Nullean.Argh.UriSchemeAttribute":
					if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Kind == TypedConstantKind.Array)
					{
						var schemes = attr.ConstructorArguments[0].Values
							.Select(v => v.Value as string).Where(s => s is not null).Select(s => s!)
							.ToImmutableArray();
						if (!schemes.IsEmpty) builder.Add(new UriSchemeConstraint(schemes));
					}
					break;
				case "global::Nullean.Argh.ExistingAttribute":
					if (fsKind is CliScalarKind.FileInfo or CliScalarKind.DirectoryInfo)
						builder.Add(new ExistingPathConstraint());
					break;
				case "global::Nullean.Argh.NonExistingAttribute":
					if (fsKind is CliScalarKind.FileInfo or CliScalarKind.DirectoryInfo)
						builder.Add(new NonExistingPathConstraint());
					break;
				case "global::Nullean.Argh.RejectSymbolicLinksAttribute":
					if (fsKind is CliScalarKind.FileInfo or CliScalarKind.DirectoryInfo)
						builder.Add(new RejectSymbolicLinksConstraint());
					break;
			}
		}
		return OrderPathValidations(builder.ToImmutable());
	}

	private static ImmutableArray<ValidationConstraint> OrderPathValidations(ImmutableArray<ValidationConstraint> validations)
	{
		if (validations.IsDefaultOrEmpty)
			return validations;

		var hasReject = false;
		foreach (var c in validations)
		{
			if (c is RejectSymbolicLinksConstraint)
			{
				hasReject = true;
				break;
			}
		}

		if (!hasReject)
			return validations;

		var b = ImmutableArray.CreateBuilder<ValidationConstraint>(validations.Length);
		foreach (var c in validations)
		{
			if (c is RejectSymbolicLinksConstraint)
				b.Add(c);
		}

		foreach (var c in validations)
		{
			if (c is not RejectSymbolicLinksConstraint)
				b.Add(c);
		}

		return b.ToImmutable();
	}

	private static string ResolveEnumMemberCliName(ImmutableArray<string> cliNames, int index, string memberName)
		=> !cliNames.IsDefaultOrEmpty ? cliNames[index] : memberName.ToLowerInvariant();

	private static string? BuildValidationLine(ParameterModel p)
	{
		var tokens = new List<string>();

		if (p.ScalarKind == CliScalarKind.Enum && !p.EnumMemberNames.IsDefaultOrEmpty)
		{
			tokens.Add("One of: <" + string.Join("|", p.EnumMemberNames.Select((m, i) => ResolveEnumMemberCliName(p.EnumMemberCliNames, i, m))) + ">");
			if (p.EnumMemberDocs is { Count: > 0 } docs)
			{
				var memberDescParts = new List<string>();
				for (var i = 0; i < p.EnumMemberNames.Length; i++)
				{
					var member = p.EnumMemberNames[i];
					var cliName = ResolveEnumMemberCliName(p.EnumMemberCliNames, i, member);
					if (docs.TryGetValue(member, out var memberDoc) && !string.IsNullOrWhiteSpace(memberDoc))
						memberDescParts.Add($"{cliName}: {memberDoc.Trim()}");
				}
				if (memberDescParts.Count > 0)
					tokens.Add("(" + string.Join("; ", memberDescParts) + ")");
			}
		}

		if (p.IsCollection && p.ElementScalarKind == CliScalarKind.Enum && !p.ElementEnumMemberNames.IsDefaultOrEmpty)
		{
			var label = p.CollectionTargetIsReadOnlySet ? "Combination of:" : "One or more of:";
			tokens.Add(label + " <" + string.Join("|", p.ElementEnumMemberNames.Select((m, i) => ResolveEnumMemberCliName(p.ElementEnumMemberCliNames, i, m))) + ">");
			if (p.ElementEnumMemberDocs is { Count: > 0 } elemDocs)
			{
				var memberDescParts = new List<string>();
				for (var i = 0; i < p.ElementEnumMemberNames.Length; i++)
				{
					var member = p.ElementEnumMemberNames[i];
					var cliName = ResolveEnumMemberCliName(p.ElementEnumMemberCliNames, i, member);
					if (elemDocs.TryGetValue(member, out var memberDoc) && !string.IsNullOrWhiteSpace(memberDoc))
						memberDescParts.Add($"{cliName}: {memberDoc.Trim()}");
				}
				if (memberDescParts.Count > 0)
					tokens.Add("(" + string.Join("; ", memberDescParts) + ")");
			}
		}

		if (!p.Validations.IsDefaultOrEmpty)
		{
			foreach (var v in p.Validations)
			{
				switch (v)
				{
					case RangeConstraint r:
						tokens.Add($"[range: {r.MinLiteral.Trim('"')}–{r.MaxLiteral.Trim('"')}]");
						break;
					case CollectionCountConstraint cc when cc.Min.HasValue && cc.Max.HasValue:
						tokens.Add($"[count: {cc.Min}–{cc.Max}]");
						break;
					case CollectionCountConstraint cc when cc.Min.HasValue:
						tokens.Add($"[min-count: {cc.Min}]");
						break;
					case CollectionCountConstraint cc when cc.Max.HasValue:
						tokens.Add($"[max-count: {cc.Max}]");
						break;
					case StringLengthConstraint s when s.Min.HasValue && s.Max.HasValue:
						tokens.Add($"[length: {s.Min}–{s.Max}]");
						break;
					case StringLengthConstraint s when s.Min.HasValue:
						tokens.Add($"[min-length: {s.Min}]");
						break;
					case StringLengthConstraint s when s.Max.HasValue:
						tokens.Add($"[max-length: {s.Max}]");
						break;
					case RegexConstraint rx:
						tokens.Add($"[pattern: {rx.Pattern}]");
						break;
					case AllowedValuesConstraint av:
						tokens.Add("[allowed: " + string.Join("|", av.Values.Select(val => val.Trim('"'))) + "]");
						break;
					case DeniedValuesConstraint dv:
						tokens.Add("[denied: " + string.Join("|", dv.Values.Select(val => val.Trim('"'))) + "]");
						break;
					case EmailConstraint:
						tokens.Add("[email]");
						break;
					case UrlConstraint:
						tokens.Add("[url]");
						break;
					case UriSchemeConstraint us:
						tokens.Add("[schemes: " + string.Join("|", us.Schemes) + "]");
						break;
					case FileExtensionsConstraint fe:
						tokens.Add("[extensions: " + string.Join("|", fe.Extensions) + "]");
						break;
					case ExistingPathConstraint:
						tokens.Add("[existing]");
						break;
					case NonExistingPathConstraint:
						tokens.Add("[unused path]");
						break;
					case RejectSymbolicLinksConstraint:
						tokens.Add("[no symlinks]");
						break;
					case TimeSpanRangeConstraint ts:
						tokens.Add($"[time-span-range: {ts.MinLiteral.Trim('"')}–{ts.MaxLiteral.Trim('"')}]");
						break;
				}
			}
		}

		if (p.ExpandUserProfileBeforeBind)
			tokens.Add("[expand ~ profile]");

		return tokens.Count > 0 ? string.Join(" ", tokens) : null;
	}

	private abstract record ValidationConstraint;
	private sealed record CollectionCountConstraint(int? Min, int? Max) : ValidationConstraint;
	private sealed record RangeConstraint(string MinLiteral, string MaxLiteral) : ValidationConstraint;
	private sealed record TimeSpanRangeConstraint(string MinLiteral, string MaxLiteral) : ValidationConstraint;
	private sealed record StringLengthConstraint(int? Min, int? Max) : ValidationConstraint;
	private sealed record RegexConstraint(string Pattern) : ValidationConstraint;
	private sealed record AllowedValuesConstraint(ImmutableArray<string> Values) : ValidationConstraint;
	private sealed record DeniedValuesConstraint(ImmutableArray<string> Values) : ValidationConstraint;
	private sealed record EmailConstraint : ValidationConstraint;
	private sealed record UrlConstraint : ValidationConstraint;
	private sealed record UriSchemeConstraint(ImmutableArray<string> Schemes) : ValidationConstraint;
	private sealed record FileExtensionsConstraint(ImmutableArray<string> Extensions) : ValidationConstraint;
	private sealed record ExistingPathConstraint : ValidationConstraint;
	private sealed record NonExistingPathConstraint : ValidationConstraint;
	private sealed record RejectSymbolicLinksConstraint : ValidationConstraint;

}
