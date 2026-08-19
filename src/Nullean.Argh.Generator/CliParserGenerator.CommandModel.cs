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
	private sealed record CommandModel(
		ImmutableArray<string> RoutePrefix,
		string CommandName,
		string RunMethodName,
		string ContainingTypeFq,
		string MethodName,
		bool RequiresInstance,
		bool ContainingTypeHasParameterlessCtor,
		string ReturnTypeFq,
		bool ReturnIsAsync,
		bool ReturnIsVoid,
		ImmutableArray<ParameterModel> Parameters,
		bool HandlerHasNoOptionsInjection,
		ImmutableArray<HandlerParam> HandlerParamTypes,
		SourceSpanInfo HandlerSpanInfo,
		ImmutableArray<(string Name, string TypeMetadataName)> ContainingTypeCtorParams,
		string HandlerDocCommentId,
		string SummaryOneLiner,
		string RemarksRendered,
		string SummaryInnerXml,
		string RemarksInnerXml,
		string ExamplesRendered,
		string UsageHints,
		ImmutableArray<(string Fq, bool HasParameterlessCtor)> CommandMiddlewareData,
		bool IsRootDefault = false,
		bool IsLambda = false,
		string LambdaStorageKey = "",
		string LambdaDelegateFq = "",
		bool IsIntrinsic = false,
		ImmutableArray<string> CommandAliases = default,
		bool IsHidden = false,
		bool IsDeprecated = false,
		string? DeprecationMessage = null,
		CommandIntentData? Intent = null,
		CommandOutputData? Output = null)
	{


		/// <summary>Overload for the per-invocation Select step — uses <see cref="DiagnosticAccumulator"/> instead of SourceProductionContext.</summary>
		public static CommandModel FromRootMethod(
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			ImmutableArray<string> routePrefix,
			DiagnosticAccumulator acc,
			Location diagnosticLocation,
			Compilation? compilation = null)
		{
			var parameters = BuildParameterModels(method, parseOptions, acc, diagnosticLocation, compilation);
			ReportDuplicateCliNamesAcc(acc, diagnosticLocation, parameters);
			ReportBoolNegationSwitchConflictsAcc(acc, diagnosticLocation, parameters, method);
			ValidateExpandedParameterLayoutAcc(acc, diagnosticLocation, parameters);
			ValidateVariadicPositionalIsLastAcc(acc, diagnosticLocation, parameters);
			foreach (var p in parameters)
			{
				if (p.IsCollection && p.Kind == ParameterKind.Positional && !p.IsVariadic)
					acc.Add(CollectionPositionalNotSupported, diagnosticLocation);
				if (p.IsVariadic && !p.CollectionTargetIsArray)
					acc.Add(VariadicCollectionMustBeArray, diagnosticLocation);
				if (p.CollectionTargetIsReadOnlySet && !p.ElementIsValueType)
					acc.Add(ReadOnlySetInvalidElementType, diagnosticLocation, p.ElementTypeName);
			}
			var docs = MergeMethodDocumentationFromTrivia(method, Documentation.ParseMethod(method.GetDocumentationCommentXml(), parseOptions), parseOptions);
			var withDocs = ApplyParamDocumentation(parameters, method, docs.ParamDocsRaw);
			withDocs = ApplyCollectionSeparatorsFromDocumentation(withDocs, method, docs.ParamSeparators);
			var usage = UsageSynopsis.Build(withDocs);
			var runName = BuildRootDefaultRunMethodName(routePrefix);
			var containingFq = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			var hasParamlessCtor = method.ContainingType is INamedTypeSymbol namedCt && HasPublicParameterlessCtor(namedCt);
			var (retFq, retIsAsync, retIsVoid, handlerNoInj, handlerParams, handlerLoc, ctorParams, mwData, docId) = ExtractHandlerAnalysis(method);
			return new CommandModel(routePrefix, RootDefaultInternalCommandName, runName, containingFq, method.Name, !method.IsStatic, hasParamlessCtor, retFq, retIsAsync, retIsVoid, withDocs, handlerNoInj, handlerParams, handlerLoc, ctorParams, docId, docs.SummaryOneLiner, docs.RemarksRendered, docs.SummaryInnerXml, docs.RemarksInnerXml, docs.ExamplesRendered, usage, mwData, IsRootDefault: true);
		}

		/// <summary>Overload for the per-invocation Select step — uses <see cref="DiagnosticAccumulator"/> instead of SourceProductionContext.</summary>
		public static CommandModel FromMethod(
			string commandName,
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			ImmutableArray<string> routePrefix,
			DiagnosticAccumulator acc,
			Location diagnosticLocation,
			Compilation? compilation = null)
		{
			var parameters = BuildParameterModels(method, parseOptions, acc, diagnosticLocation, compilation);
			ReportDuplicateCliNamesAcc(acc, diagnosticLocation, parameters);
			ReportBoolNegationSwitchConflictsAcc(acc, diagnosticLocation, parameters, method);
			ValidateExpandedParameterLayoutAcc(acc, diagnosticLocation, parameters);
			ValidateVariadicPositionalIsLastAcc(acc, diagnosticLocation, parameters);
			foreach (var p in parameters)
			{
				if (p.IsCollection && p.Kind == ParameterKind.Positional && !p.IsVariadic)
					acc.Add(CollectionPositionalNotSupported, diagnosticLocation);
				if (p.IsVariadic && !p.CollectionTargetIsArray)
					acc.Add(VariadicCollectionMustBeArray, diagnosticLocation);
				if (p.CollectionTargetIsReadOnlySet && !p.ElementIsValueType)
					acc.Add(ReadOnlySetInvalidElementType, diagnosticLocation, p.ElementTypeName);
			}
			var docs = MergeMethodDocumentationFromTrivia(method, Documentation.ParseMethod(method.GetDocumentationCommentXml(), parseOptions), parseOptions);
			var withDocs = ApplyParamDocumentation(parameters, method, docs.ParamDocsRaw);
			withDocs = ApplyCollectionSeparatorsFromDocumentation(withDocs, method, docs.ParamSeparators);
			var usage = UsageSynopsis.Build(withDocs);
			var runName = BuildRunMethodName(routePrefix, commandName);
			var containingFq = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			var hasParamlessCtor = method.ContainingType is INamedTypeSymbol namedCt && HasPublicParameterlessCtor(namedCt);
			var (retFq, retIsAsync, retIsVoid, handlerNoInj, handlerParams, handlerLoc, ctorParams, mwData, docId) = ExtractHandlerAnalysis(method);
			var (isDeprecated, deprecationMsg) = TryGetObsoleteAttribute(method);
			return new CommandModel(routePrefix, commandName, runName, containingFq, method.Name, !method.IsStatic, hasParamlessCtor, retFq, retIsAsync, retIsVoid, withDocs, handlerNoInj, handlerParams, handlerLoc, ctorParams, docId, docs.SummaryOneLiner, docs.RemarksRendered, docs.SummaryInnerXml, docs.RemarksInnerXml, docs.ExamplesRendered, usage, mwData, IsIntrinsic: HasCommandIntrinsicAttribute(method), CommandAliases: TryGetCommandAliasesFromAttribute(method), IsHidden: HasHiddenAttribute(method), IsDeprecated: isDeprecated, DeprecationMessage: deprecationMsg, Intent: TryGetCommandIntentData(method), Output: BuildCommandOutputFromParameters(withDocs));
		}

		private static ImmutableArray<ParameterModel> BuildParameterModels(
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			DiagnosticAccumulator acc,
			Location diagnosticLocation,
			Compilation? compilation = null)
		{
			var builder = ImmutableArray.CreateBuilder<ParameterModel>();
			foreach (var p in method.Parameters)
			{
				if (IsInjected(p))
				{
					builder.Add(ParameterModel.From(p));
					continue;
				}
				if (HasAsParametersAttribute(p))
				{
					if (p.Type is not INamedTypeSymbol namedType || namedType.TypeKind == TypeKind.Error)
						continue;
					var prefix = GetAsParametersPrefix(p);
					foreach (var pm in FlattenAsParametersTypeAcc(acc, diagnosticLocation, p, namedType, prefix, compilation, parseOptions))
						builder.Add(pm);
					continue;
				}
				builder.Add(ParameterModel.From(p, acc, diagnosticLocation));
			}
			return builder.ToImmutable();
		}


		private static ImmutableArray<ParameterModel> ApplyCollectionSeparatorsFromDocumentation(
			ImmutableArray<ParameterModel> parameters,
			IMethodSymbol method,
			ImmutableDictionary<string, string> paramSeparators)
		{
			if (paramSeparators.IsEmpty)
				return parameters;

			var b = ImmutableArray.CreateBuilder<ParameterModel>(parameters.Length);
			foreach (var p in parameters)
			{
				if (!p.IsCollection || p.CollectionSeparator is not null)
				{
					b.Add(p);
					continue;
				}

				if (paramSeparators.TryGetValue(p.SymbolName, out var sep) && !string.IsNullOrWhiteSpace(sep))
					b.Add(p with { CollectionSeparator = sep });
				else
					b.Add(p);
			}

			return b.ToImmutable();
		}

		private static ImmutableArray<INamedTypeSymbol> CollectCommandMiddleware(IMethodSymbol method)
		{
			var b = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
			foreach (var attr in method.GetAttributes())
			{
				var ac = attr.AttributeClass;
				if (ac is null || ac.Name != "MiddlewareAttribute" || ac.TypeArguments.Length != 1)
					continue;
				if (ac.TypeArguments[0] is INamedTypeSymbol ft && ft.TypeKind != TypeKind.Error)
					b.Add(ft);
			}

			return b.ToImmutable();
		}

		/// <summary>Returns the CSharp-error-message display string for a type — used as a stable, symbol-free metadata key.</summary>
		private static string GetMetadataName(ITypeSymbol t) =>
			t.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

		private static (
			string ReturnTypeFq,
			bool ReturnIsAsync,
			bool ReturnIsVoid,
			bool HasNoOptionsInjection,
			ImmutableArray<HandlerParam> HandlerParamTypes,
			SourceSpanInfo HandlerSpanInfo,
			ImmutableArray<(string Name, string TypeMetadataName)> ContainingTypeCtorParams,
			ImmutableArray<(string Fq, bool HasParameterlessCtor)> MiddlewareData,
			string DocCommentId
		) ExtractHandlerAnalysis(IMethodSymbol method)
		{
			// Return type
			var retFq = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			// FullyQualifiedFormat renders special types via their C# keyword ("void"), not "global::System.Void".
			var retIsVoid = retFq is "void"
				or "global::System.Threading.Tasks.Task"
				or "global::System.Threading.Tasks.ValueTask";
			var retIsAsync = retFq is "global::System.Threading.Tasks.Task"
				or "global::System.Threading.Tasks.ValueTask"
				|| (method.ReturnType is INamedTypeSymbol named && named.IsGenericType &&
				    (named.ConstructedFrom.Name is "Task" or "ValueTask") &&
				    named.ConstructedFrom.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks");

			// Parameters
			var paramBuilder = ImmutableArray.CreateBuilder<HandlerParam>(method.Parameters.Length);
			foreach (var p in method.Parameters)
			{
				var isInj = IsInjected(p);
				var isAsParam = HasAsParametersAttribute(p);
				var asParamPrefix = isAsParam ? GetAsParametersPrefix(p) : null;
				string? asParamTypeFq = null;
				ImmutableArray<string>? asParamBestCtor = null;
				var asParamIsPublic = true;
				var asParamIsGeneric = false;
				if (isAsParam && p.Type is INamedTypeSymbol asNt && asNt.TypeKind != TypeKind.Error)
				{
					asParamTypeFq = asNt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
					asParamIsPublic = asNt.DeclaredAccessibility == Accessibility.Public;
					asParamIsGeneric = asNt.TypeParameters.Length > 0;
					// Pre-compute the best ctor param order for DTO construction in emit.
					var membersForCtor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					foreach (var member in asNt.GetMembers())
					{
						if (member is IPropertySymbol prop && prop.DeclaredAccessibility == Accessibility.Public && !prop.IsStatic && !prop.IsIndexer && prop.GetMethod is not null && prop.SetMethod is not null)
							membersForCtor.Add(prop.Name);
						else if (member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic)
							membersForCtor.Add(field.Name);
					}
					// Walk primary ctor or most-parameterized public ctor
					IMethodSymbol? bestCtor = null;
					foreach (var ctor in asNt.InstanceConstructors)
					{
						if (ctor.DeclaredAccessibility != Accessibility.Public) continue;
						if (ctor.Parameters.Length == 0) continue;
						if (!ctor.Parameters.All(cp => membersForCtor.Contains(cp.Name))) continue;
						if (bestCtor is null || ctor.Parameters.Length > bestCtor.Parameters.Length)
							bestCtor = ctor;
					}
					if (bestCtor is not null)
					{
						var ctorB = ImmutableArray.CreateBuilder<string>(bestCtor.Parameters.Length);
						foreach (var cp in bestCtor.Parameters)
							ctorB.Add(cp.Name);
						asParamBestCtor = ctorB.MoveToImmutable();
					}
				}
				var paramBaseNames = p.Type is INamedTypeSymbol paramNt
				? CollectBaseTypeMetadataNames(paramNt)
				: ImmutableArray<string>.Empty;
			paramBuilder.Add(new HandlerParam(p.Name, GetMetadataName(p.Type), paramBaseNames, isInj, isAsParam, asParamPrefix, asParamTypeFq, asParamIsPublic, asParamIsGeneric, asParamBestCtor));
			}

			// Handler location
			var loc = method.Locations.FirstOrDefault() ?? Location.None;

			// Primary constructor parameters of containing type
			var ctorParams = ImmutableArray<(string, string)>.Empty;
			var primaryCtor = TryGetPrimaryConstructor(method.ContainingType);
			if (primaryCtor is not null && primaryCtor.Parameters.Length > 0)
			{
				var ctorBuilder = ImmutableArray.CreateBuilder<(string, string)>(primaryCtor.Parameters.Length);
				foreach (var cp in primaryCtor.Parameters)
					ctorBuilder.Add((cp.Name, GetMetadataName(cp.Type)));
				ctorParams = ctorBuilder.ToImmutable();
			}

			// Middleware data
			var rawMiddleware = CollectCommandMiddleware(method);
			var mwBuilder = ImmutableArray.CreateBuilder<(string, bool)>(rawMiddleware.Length);
			foreach (var mw in rawMiddleware)
				mwBuilder.Add((mw.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), HasPublicParameterlessCtor(mw)));
			var middlewareData = mwBuilder.ToImmutable();

			var docId = method.GetDocumentationCommentId() ?? "";
			return (
				ReturnTypeFq: retFq,
				ReturnIsAsync: retIsAsync,
				ReturnIsVoid: retIsVoid,
				HasNoOptionsInjection: HasNoOptionsInjection(method),
				HandlerParamTypes: paramBuilder.ToImmutable(),
				HandlerSpanInfo: SourceSpanInfo.From(loc),
				ContainingTypeCtorParams: ctorParams,
				MiddlewareData: middlewareData,
				DocCommentId: docId
			);
		}

		private static string BuildRunMethodName(ImmutableArray<string> routePrefix, string commandName)
		{
			if (routePrefix.IsDefaultOrEmpty)
				return "Run_" + Naming.SanitizeIdentifier(commandName);

			var sb = new StringBuilder();
			sb.Append("Run");
			foreach (var seg in routePrefix)
			{
				sb.Append('_');
				sb.Append(Naming.SanitizeIdentifier(seg));
			}

			sb.Append('_');
			sb.Append(Naming.SanitizeIdentifier(commandName));
			return sb.ToString();
		}

		/// <summary>Visible to <see cref="CliParserGenerator"/> for lambda root commands (same naming as <see cref="FromRootMethod"/>).</summary>
		internal static string BuildRootDefaultRunMethodName(ImmutableArray<string> routePrefix) =>
			BuildRunMethodName(routePrefix, "RootDefault");

		/// <summary>Public helper used by the analyzed-invocation pipeline to re-compute run method names when prefixing.</summary>
		internal static string BuildRunMethodNameStatic(ImmutableArray<string> routePrefix, string commandName) =>
			BuildRunMethodName(routePrefix, commandName);

		private static ImmutableArray<ParameterModel> ApplyParamDocumentation(
			ImmutableArray<ParameterModel> parameters,
			IMethodSymbol method,
			ImmutableDictionary<string, string> paramDocsRaw)
		{
			if (paramDocsRaw.IsEmpty)
				return parameters;

			var map = new Dictionary<string, ParameterModel>();
			foreach (var p in parameters)
				map[p.SymbolName] = p;

			foreach (var ps in method.Parameters)
			{
				if (!map.TryGetValue(ps.Name, out var existing))
					continue;
				if (!paramDocsRaw.TryGetValue(ps.Name, out var raw) || string.IsNullOrWhiteSpace(raw))
					continue;

				if (existing.Kind == ParameterKind.Positional)
				{
					map[ps.Name] = existing with { Description = raw.Trim() };
					continue;
				}

				var doc = ParamDocParser.Parse(raw);
				map[ps.Name] = existing with
				{
					CliLongName = doc.ExplicitLongName ?? existing.CliLongName,
					Description = doc.Description,
					ShortOpt = doc.ShortOpt,
					Aliases = doc.Aliases
				};
			}

			var rebuilt = ImmutableArray.CreateBuilder<ParameterModel>(parameters.Length);
			foreach (var p in parameters)
				rebuilt.Add(map[p.SymbolName]);

			return rebuilt.ToImmutable();
		}
	}

	/// <summary>Parse options property/field <c>&lt;summary&gt;</c> lines that may start with <c>-x, --long, …</c> synopsis prefixes (same rules as handler <paramref/> docs).</summary>
	private static ParamDoc ParseOptionsFlagDocumentation(string? summaryLine)
	{
		if (string.IsNullOrWhiteSpace(summaryLine))
			return new ParamDoc(null, ImmutableArray<string>.Empty, "");
		return ParamDocParser.Parse(summaryLine!.Trim());
	}

	// ── Validation constraint types ─────────────────────────────────────────────
	// All fields are value types, strings, or ImmutableArray<string> so these can be
	// cached inside AnalyzedInvocation records in the Roslyn incremental pipeline.

}
