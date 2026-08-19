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
	private sealed record ParameterModel(
		string SymbolName,
		string LocalVarName,
		string CliLongName,
		ParameterKind Kind,
		BoolSpecialKind Special,
		CliScalarKind ScalarKind,
		string TypeName,
		string? EnumTypeFq,
		ImmutableArray<string> EnumMemberNames,
		string? ParserTypeFq,
		string? CustomValueTypeFq,
		bool IsRequired,
		string? DefaultValueLiteral,
		string Description,
		char? ShortOpt,
		ImmutableArray<string> Aliases,
		bool IsCollection = false,
		string? CollectionSeparator = null,
		CliScalarKind ElementScalarKind = CliScalarKind.Primitive,
		string ElementTypeName = "string",
		string? ElementEnumTypeFq = null,
		ImmutableArray<string> ElementEnumMemberNames = default,
		ImmutableArray<string> EnumMemberCliNames = default,
		ImmutableArray<string> ElementEnumMemberCliNames = default,
		string? ElementParserTypeFq = null,
		string? ElementCustomValueTypeFq = null,
		string? FullDeclaredTypeFq = null,
		string? AsParametersOwnerParamName = null,
		int AsParametersMemberOrder = -1,
		string? AsParametersTypeFq = null,
		bool AsParametersUseInit = false,
		string? AsParametersClrName = null,
		bool CollectionTargetIsArray = false,
		bool CollectionTargetIsReadOnlySet = false,
		/// <summary>True when the declared collection type uses NRT annotation (e.g. <c>IReadOnlySet&lt;int&gt;?</c>). Optional params with this shape default to null when no values were parsed.</summary>
		bool DeclaredNullableAnnotated = false,
		bool ElementIsValueType = false,
		ImmutableDictionary<string, string>? EnumMemberDocs = null,
		ImmutableDictionary<string, string>? ElementEnumMemberDocs = null,
		bool ExpandUserProfileBeforeBind = false,
		ImmutableArray<ValidationConstraint> Validations = default,
		bool IsHidden = false,
		bool IsVariadic = false,
		/// <summary>
		/// True when the property is from a cross-assembly type (DeclaringSyntaxReferences empty) and has no
		/// detectable static default. The emit uses <c>new T().PropName</c> at runtime for the initial value.
		/// </summary>
		bool UsesRuntimeDefault = false,
		/// <summary>
		/// True when the source property/parameter is a nullable reference type (NRT, e.g. <c>string?</c>,
		/// <c>FileInfo?</c>) as opposed to a value-type <c>Nullable&lt;T&gt;</c> (e.g. <c>int?</c>).
		/// Used by <see cref="GetCSharpCliType"/> to emit <c>string?</c> even when <see cref="UsesRuntimeDefault"/>
		/// is true, preventing CS8600 when the runtime default for a nullable property is <c>null</c>.
		/// </summary>
		bool IsNullableAnnotated = false,
		bool IsConfirmationSkip = false,
		bool IsDryRun = false,
		bool IsCommandOutput = false,
		ImmutableArray<string> CommandOutputExplicitFormats = default,
		bool IsDeprecated = false,
		string? DeprecationMessage = null)
	{
		// ── shared helpers ──────────────────────────────────────────────────────

		private static ImmutableDictionary<string, string>? TryGetEnumDocs(ITypeSymbol type)
		{
			var t = type;
			if (t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nul)
				t = nul.TypeArguments[0];
			return t is INamedTypeSymbol en ? GetEnumMemberDocs(en) : null;
		}

		private static void ClassifyScalarUnified(
			ITypeSymbol type,
			ISymbol attributeHost,
			BoolSpecialKind bs,
			bool isSeparateType,
			out CliScalarKind sk,
			out string typeName,
			out string? enumFq,
			out ImmutableArray<string> enumMembers,
			out string? parserFq,
			out string? customValueTypeFq)
		{
			if (isSeparateType)
				ClassifyScalarForType(type, attributeHost, bs, out sk, out typeName, out enumFq, out enumMembers, out parserFq, out customValueTypeFq);
			else
				ClassifyScalar((IParameterSymbol)attributeHost, bs, out sk, out typeName, out enumFq, out enumMembers, out parserFq, out customValueTypeFq);
		}

		private static ParameterModel BuildCollectionParameterModel(
			ITypeSymbol collectionType,
			ITypeSymbol elementType,
			ISymbol attributeHost,
			ParameterKind kind,
			string cliLongName,
			string localVarName,
			string symbolName,
			bool isSeparateType,
			string? defaultLiteral,
			string description,
			AsParametersMeta? asParams,
			char? flagShortOpt = null,
			ImmutableArray<string> synopsisAliasesFromSummary = default,
			bool isVariadic = false,
			DiagnosticAccumulator? reportAcc = null,
			Location? reportFallbackLocation = null)
		{
			ClassifyScalarForType(elementType, attributeHost, BoolSpecialKind.None,
				out var elemSk, out var elemTn, out var eFq, out var eMem, out var pFq, out var cFq);
			var eCliMem = elemSk == CliScalarKind.Enum ? TryGetEnumCliNames(elementType) : default;
			var elemEnumDocs = elemSk == CliScalarKind.Enum ? TryGetEnumDocs(elementType) : null;
			if (reportAcc is not null)
				ReportFilesystemPathAttributeIssues(attributeHost, CliScalarKind.Collection, symbolName, reportAcc,
					reportFallbackLocation, filesystemScalarKind: elemSk);
			var sep = TryGetCollectionSeparatorFromAttribute(attributeHost);
			var required = isSeparateType
				? ComputeRequiredForOptionsType(collectionType, BoolSpecialKind.None)
				: ComputeRequired((IParameterSymbol)attributeHost, BoolSpecialKind.None);
			var defFq = (collectionType as INamedTypeSymbol)?.OriginalDefinition
				.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
			var synopsisAliasesResolved = synopsisAliasesFromSummary.IsDefault
				? ImmutableArray<string>.Empty
				: synopsisAliasesFromSummary;
			var fq = collectionType.ToDisplayString(FullyQualifiedFormatWithNullableRefAnnotations);
			var declaredNullableAnnotated = collectionType.NullableAnnotation == NullableAnnotation.Annotated;
			var collValidations = ReadValidationConstraints(attributeHost, CliScalarKind.Collection, "values", isCollection: true,
				filesystemScalarKind: elemSk);
			var expandProfileElem = TryReadExpandUserProfileBeforeBind(attributeHost, elemSk);
			// Variadic positionals always allow zero items by C# params convention.
			// Minimum count enforcement is handled via CollectionCountConstraint ([MinLength]).
			if (isVariadic) required = false;
			var (isOutputColl, outputFormatsColl) = TryGetCommandOutputAttribute(attributeHost);
			var (isDeprecatedColl, deprecationMsgColl) = TryGetObsoleteAttribute(attributeHost);
			return new ParameterModel(
				symbolName,
				localVarName,
				cliLongName,
				kind,
				BoolSpecialKind.None,
				CliScalarKind.Collection,
				"values",
				null,
				ImmutableArray<string>.Empty,
				null,
				null,
				required,
				defaultLiteral,
				description,
				flagShortOpt,
				synopsisAliasesResolved,
				IsCollection: true,
				CollectionSeparator: sep,
				ElementScalarKind: elemSk,
				ElementTypeName: elemTn,
				ElementEnumTypeFq: eFq,
				ElementEnumMemberNames: eMem,
				ElementEnumMemberCliNames: eCliMem,
				ElementParserTypeFq: pFq,
				ElementCustomValueTypeFq: cFq,
				FullDeclaredTypeFq: fq,
				CollectionTargetIsArray: collectionType is IArrayTypeSymbol,
				CollectionTargetIsReadOnlySet: defFq == "global::System.Collections.Generic.IReadOnlySet<T>",
				DeclaredNullableAnnotated: declaredNullableAnnotated,
				ElementIsValueType: elementType.IsValueType,
				ElementEnumMemberDocs: elemEnumDocs,
				AsParametersOwnerParamName: asParams?.OwnerParamName,
				AsParametersMemberOrder: asParams?.MemberOrder ?? -1,
				AsParametersTypeFq: asParams?.TypeFq,
				AsParametersUseInit: asParams?.UseInit ?? false,
				AsParametersClrName: asParams?.ClrName,
				ExpandUserProfileBeforeBind: expandProfileElem,
				Validations: collValidations,
				IsHidden: HasHiddenAttribute(attributeHost),
				IsVariadic: isVariadic,
				IsConfirmationSkip: HasConfirmationSkipAttribute(attributeHost),
				IsDryRun: HasDryRunAttribute(attributeHost),
				IsCommandOutput: isOutputColl,
				CommandOutputExplicitFormats: outputFormatsColl,
				IsDeprecated: isDeprecatedColl,
				DeprecationMessage: deprecationMsgColl);
		}

		// ── five factory methods ─────────────────────────────────────────────

		public static ParameterModel From(IParameterSymbol p, DiagnosticAccumulator? reportAcc = null,
			Location? reportFallbackLocation = null)
		{
			var isArg = HasArgumentAttribute(p);

			if (IsInjectedStatic(p))
				return new ParameterModel(
					p.Name,
					SafeLocalName(p.Name),
					Naming.ToCliLongName(p.Name),
					ParameterKind.Injected,
					BoolSpecialKind.None,
					CliScalarKind.Primitive,
					"CancellationToken",
					null,
					ImmutableArray<string>.Empty,
					null,
					null,
					false,
					null,
					"",
					null,
					ImmutableArray<string>.Empty);

			var kind = isArg ? ParameterKind.Positional : ParameterKind.Flag;
			var bs = ClassifyBool(p.Type);
			if (TryUnwrapCollectionType(p.Type, out var elemType) && bs == BoolSpecialKind.None
				&& TryParserTypeFqFromSymbol(p) is null)
			{
				var isVariadic = isArg && p.Type is IArrayTypeSymbol;
				var defLitColl = TryGetDefaultLiteral(p, BoolSpecialKind.None);
				return BuildCollectionParameterModel(p.Type, elemType, p, kind,
					Naming.ToCliLongName(p.Name), SafeLocalName(p.Name), p.Name,
					isSeparateType: false, defLitColl, "", asParams: null, isVariadic: isVariadic,
					reportAcc: reportAcc, reportFallbackLocation: reportFallbackLocation);
			}

			ClassifyScalarUnified(p.Type, p, bs, isSeparateType: false,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			if (reportAcc is not null)
				ReportFilesystemPathAttributeIssues(p, sk, p.Name, reportAcc, reportFallbackLocation);

			var required = ComputeRequired(p, bs);
			var defLit = TryGetDefaultLiteral(p, bs);
			var enumDocs = sk == CliScalarKind.Enum ? TryGetEnumDocs(p.Type) : null;
			var enumCliNames = sk == CliScalarKind.Enum ? TryGetEnumCliNames(p.Type) : default;
			var validations = ReadValidationConstraints(p, sk, typeName);
			var expandProf = TryReadExpandUserProfileBeforeBind(p, sk);
			var (isOutputP, outputFormatsP) = TryGetCommandOutputAttribute(p);
			var (isDeprecatedP, deprecationMsgP) = TryGetObsoleteAttribute(p);
			return new ParameterModel(
				p.Name,
				SafeLocalName(p.Name),
				Naming.ToCliLongName(p.Name),
				kind,
				bs,
				sk,
				typeName,
				enumFq,
				enumMembers,
				parserFq,
				customValFq,
				required,
				defLit,
				"",
				null,
				ImmutableArray<string>.Empty,
				EnumMemberCliNames: enumCliNames,
				EnumMemberDocs: enumDocs,
				ExpandUserProfileBeforeBind: expandProf,
				Validations: validations,
				IsHidden: HasHiddenAttribute(p),
				IsConfirmationSkip: HasConfirmationSkipAttribute(p),
				IsDryRun: HasDryRunAttribute(p),
				IsCommandOutput: isOutputP,
				CommandOutputExplicitFormats: outputFormatsP,
				IsDeprecated: isDeprecatedP,
				DeprecationMessage: deprecationMsgP);
		}

		public static ParameterModel FromOptionsProperty(IPropertySymbol prop, Compilation? compilation = null, string? defaultValueLiteral = null)
		{
			var rawSummary = Documentation.GetPropertySummaryLine(prop, compilation, TryExtractFullDocumentationFromPropertyTrivia(prop));
			var doc = ParseOptionsFlagDocumentation(rawSummary);
			var derivedLongNameProp = Naming.ToCliLongName(prop.Name);
			var effectiveLongNameProp = doc.ExplicitLongName ?? derivedLongNameProp;
			var bs = ClassifyBool(prop.Type);
			if (TryUnwrapCollectionType(prop.Type, out var elemType) && bs == BoolSpecialKind.None
				&& TryParserTypeFqFromSymbol(prop) is null)
			{
				return BuildCollectionParameterModel(prop.Type, elemType, prop, ParameterKind.Flag,
					effectiveLongNameProp, SafeLocalName(prop.Name), prop.Name,
					isSeparateType: true, defaultLiteral: null, doc.Description, asParams: null,
					flagShortOpt: doc.ShortOpt, synopsisAliasesFromSummary: doc.Aliases);
			}

			ClassifyScalarUnified(prop.Type, prop, bs, isSeparateType: true,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			// A property initializer supplies a CLI default: the flag is not required on the command line.
			// For cross-assembly types, DeclaringSyntaxReferences is empty so we can't read the initializer
			// expression from syntax. Mark the property as using a runtime default instead of "required".
			var isCrossAssemblyDefault = defaultValueLiteral is null && prop.DeclaringSyntaxReferences.IsEmpty;
			var required = !isCrossAssemblyDefault && ComputeRequiredForOptionsType(prop.Type, bs) && defaultValueLiteral is null;
			var enumDocs = sk == CliScalarKind.Enum ? TryGetEnumDocs(prop.Type) : null;
			var enumCliNames = sk == CliScalarKind.Enum ? TryGetEnumCliNames(prop.Type) : default;
			var validations = ReadValidationConstraints(prop, sk, typeName);
			var defLit = QualifyOptionsEnumDefaultLiteral(defaultValueLiteral, sk, enumFq, enumMembers);
			var expandProf = TryReadExpandUserProfileBeforeBind(prop, sk);
			return new ParameterModel(
				prop.Name,
				SafeLocalName(prop.Name),
				effectiveLongNameProp,
				ParameterKind.Flag,
				bs,
				sk,
				typeName,
				enumFq,
				enumMembers,
				parserFq,
				customValFq,
				required,
				defLit,
				doc.Description,
				doc.ShortOpt,
				doc.Aliases,
				EnumMemberCliNames: enumCliNames,
				EnumMemberDocs: enumDocs,
				ExpandUserProfileBeforeBind: expandProf,
				Validations: validations,
				IsHidden: HasHiddenAttribute(prop),
				UsesRuntimeDefault: isCrossAssemblyDefault,
				IsNullableAnnotated: prop.Type.NullableAnnotation == NullableAnnotation.Annotated
					|| prop.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T },
				IsConfirmationSkip: HasConfirmationSkipAttribute(prop),
				IsDryRun: HasDryRunAttribute(prop),
				IsCommandOutput: TryGetCommandOutputAttribute(prop).IsOutput,
				CommandOutputExplicitFormats: TryGetCommandOutputAttribute(prop).ExplicitFormats,
				IsDeprecated: TryGetObsoleteAttribute(prop).IsDeprecated,
				DeprecationMessage: TryGetObsoleteAttribute(prop).Message);
		}

		public static ParameterModel FromOptionsField(IFieldSymbol field, Compilation? compilation = null, string? defaultValueLiteral = null)
		{
			var rawSummary = Documentation.GetFieldSummaryLine(field, compilation, TryExtractFullDocumentationFromFieldTrivia(field));
			var doc = ParseOptionsFlagDocumentation(rawSummary);
			var derivedLongNameField = Naming.ToCliLongName(field.Name);
			var effectiveLongNameField = doc.ExplicitLongName ?? derivedLongNameField;
			var bs = ClassifyBool(field.Type);
			if (TryUnwrapCollectionType(field.Type, out var elemType) && bs == BoolSpecialKind.None
				&& TryParserTypeFqFromSymbol(field) is null)
			{
				return BuildCollectionParameterModel(field.Type, elemType, field, ParameterKind.Flag,
					effectiveLongNameField, SafeLocalName(field.Name), field.Name,
					isSeparateType: true, defaultLiteral: null, doc.Description, asParams: null,
					flagShortOpt: doc.ShortOpt, synopsisAliasesFromSummary: doc.Aliases);
			}

			ClassifyScalarUnified(field.Type, field, bs, isSeparateType: true,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			var isCrossAssemblyDefault = defaultValueLiteral is null && field.DeclaringSyntaxReferences.IsEmpty;
			var required = !isCrossAssemblyDefault && ComputeRequiredForOptionsType(field.Type, bs) && defaultValueLiteral is null;
			var enumCliNames = sk == CliScalarKind.Enum ? TryGetEnumCliNames(field.Type) : default;
			var validations = ReadValidationConstraints(field, sk, typeName);
			var defLit = QualifyOptionsEnumDefaultLiteral(defaultValueLiteral, sk, enumFq, enumMembers);
			var expandProf = TryReadExpandUserProfileBeforeBind(field, sk);
			return new ParameterModel(
				field.Name,
				SafeLocalName(field.Name),
				effectiveLongNameField,
				ParameterKind.Flag,
				bs,
				sk,
				typeName,
				enumFq,
				enumMembers,
				parserFq,
				customValFq,
				required,
				defLit,
				doc.Description,
				doc.ShortOpt,
				doc.Aliases,
				EnumMemberCliNames: enumCliNames,
				ExpandUserProfileBeforeBind: expandProf,
				Validations: validations,
				IsHidden: HasHiddenAttribute(field),
				UsesRuntimeDefault: isCrossAssemblyDefault,
				IsNullableAnnotated: field.Type.NullableAnnotation == NullableAnnotation.Annotated
					|| field.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T });
		}
		public static ParameterModel FromAsParametersCtorParameter(
			string methodParamName,
			string typeFq,
			INamedTypeSymbol containingType,
			IParameterSymbol cp,
			string namePrefix,
			int memberOrder,
			Compilation? compilation,
			CSharpParseOptions parseOptions,
			DiagnosticAccumulator? reportAcc = null,
			Location? reportFallbackLocation = null)
		{
			if (IsInjectedType(cp.Type))
			{
				var injCli = namePrefix + Naming.ToCliLongName(cp.Name);
				var injLocal = SafeLocalName(methodParamName + "_" + cp.Name);
				return new ParameterModel(
					cp.Name,
					injLocal,
					injCli,
					ParameterKind.Injected,
					BoolSpecialKind.None,
					CliScalarKind.Primitive,
					"CancellationToken",
					null,
					ImmutableArray<string>.Empty,
					null,
					null,
					false,
					null,
					"",
					null,
					ImmutableArray<string>.Empty,
					AsParametersOwnerParamName: methodParamName,
					AsParametersMemberOrder: memberOrder,
					AsParametersTypeFq: typeFq,
					AsParametersUseInit: false,
					AsParametersClrName: cp.Name);
			}

			var isArg = HasArgumentAttribute(cp);
			var kind = isArg ? ParameterKind.Positional : ParameterKind.Flag;
			var bs = ClassifyBool(cp.Type);
			var cli = namePrefix + Naming.ToCliLongName(cp.Name);
			var local = SafeLocalName(methodParamName + "_" + cp.Name);
			var desc = Documentation.GetParamDocFromType(containingType, cp.Name, compilation, TryExtractFullDocumentationFromTypeTrivia(containingType));
			if (string.IsNullOrWhiteSpace(desc))
			{
				var pxml = cp.GetDocumentationCommentXml();
				if (string.IsNullOrWhiteSpace(pxml))
					pxml = Documentation.GetDocumentationXmlFromMetadataReference(cp, compilation);
				if (!string.IsNullOrWhiteSpace(pxml))
				{
					desc = Documentation.GetParamDocFromXmlFragment(pxml, cp.Name);
					if (string.IsNullOrWhiteSpace(desc))
						desc = Documentation.GetTypeSummaryLine(pxml);
				}
			}

			if (string.IsNullOrWhiteSpace(desc))
				desc = Documentation.GetTypeSummaryLine(TryExtractDocumentationFromParameterTrivia(cp));
			var meta = new AsParametersMeta(methodParamName, memberOrder, typeFq, UseInit: false, cp.Name);
			if (TryUnwrapCollectionType(cp.Type, out var elemType) && bs == BoolSpecialKind.None
				&& TryParserTypeFqFromSymbol(cp) is null)
			{
				var isVariadicCp = isArg && cp.Type is IArrayTypeSymbol;
				var defLitColl = TryGetDefaultLiteral(cp, BoolSpecialKind.None);
				return BuildCollectionParameterModel(cp.Type, elemType, cp, kind, cli, local, cp.Name,
					isSeparateType: false, defLitColl, desc, meta, isVariadic: isVariadicCp,
					reportAcc: reportAcc,
					reportFallbackLocation: cp.Locations.FirstOrDefault() ?? reportFallbackLocation);
			}

			ClassifyScalarUnified(cp.Type, cp, bs, isSeparateType: false,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			if (reportAcc is not null)
				ReportFilesystemPathAttributeIssues(cp, sk, cp.Name, reportAcc,
					cp.Locations.FirstOrDefault() ?? reportFallbackLocation);

			var required = ComputeRequired(cp, bs);
			var defLit = TryGetDefaultLiteral(cp, bs);
			var enumCliNames = sk == CliScalarKind.Enum ? TryGetEnumCliNames(cp.Type) : default;
			var validations = ReadValidationConstraints(cp, sk, typeName);
			var expandProf = TryReadExpandUserProfileBeforeBind(cp, sk);
			var (isDeprecatedCp, deprecationMsgCp) = TryGetObsoleteAttribute(cp);
			var (isOutputCp, outputFormatsCp) = TryGetCommandOutputAttribute(cp);
			return new ParameterModel(
				cp.Name,
				local,
				cli,
				kind,
				bs,
				sk,
				typeName,
				enumFq,
				enumMembers,
				parserFq,
				customValFq,
				required,
				defLit,
				desc,
				null,
				ImmutableArray<string>.Empty,
				EnumMemberCliNames: enumCliNames,
				AsParametersOwnerParamName: methodParamName,
				AsParametersMemberOrder: memberOrder,
				AsParametersTypeFq: typeFq,
				AsParametersUseInit: false,
				AsParametersClrName: cp.Name,
				ExpandUserProfileBeforeBind: expandProf,
				Validations: validations,
				IsConfirmationSkip: HasConfirmationSkipAttribute(cp),
				IsDryRun: HasDryRunAttribute(cp),
				IsCommandOutput: isOutputCp,
				CommandOutputExplicitFormats: outputFormatsCp,
				IsDeprecated: isDeprecatedCp,
				DeprecationMessage: deprecationMsgCp);
		}

		public static ParameterModel FromAsParametersInitProperty(
			string methodParamName,
			string typeFq,
			IPropertySymbol prop,
			string namePrefix,
			int memberOrder,
			Compilation? compilation,
			CSharpParseOptions parseOptions,
			DiagnosticAccumulator? reportAcc = null,
			Location? reportFallbackLocation = null)
		{
			if (IsInjectedType(prop.Type))
			{
				var injCli = namePrefix + Naming.ToCliLongName(prop.Name);
				var injLocal = SafeLocalName(methodParamName + "_" + prop.Name);
				return new ParameterModel(
					prop.Name,
					injLocal,
					injCli,
					ParameterKind.Injected,
					BoolSpecialKind.None,
					CliScalarKind.Primitive,
					"CancellationToken",
					null,
					ImmutableArray<string>.Empty,
					null,
					null,
					false,
					null,
					"",
					null,
					ImmutableArray<string>.Empty,
					AsParametersOwnerParamName: methodParamName,
					AsParametersMemberOrder: memberOrder,
					AsParametersTypeFq: typeFq,
					AsParametersUseInit: true,
					AsParametersClrName: prop.Name);
			}

			var isArg = HasArgumentAttribute(prop);
			var kind = isArg ? ParameterKind.Positional : ParameterKind.Flag;
			var bs = ClassifyBool(prop.Type);
			var local = SafeLocalName(methodParamName + "_" + prop.Name);
			var rawSummary = Documentation.GetPropertySummaryLine(prop, compilation, TryExtractFullDocumentationFromPropertyTrivia(prop));
			var doc = ParseOptionsFlagDocumentation(rawSummary);
			var derivedCli = namePrefix + Naming.ToCliLongName(prop.Name);
			var cli = doc.ExplicitLongName is not null ? namePrefix + doc.ExplicitLongName : derivedCli;
			var meta = new AsParametersMeta(methodParamName, memberOrder, typeFq, UseInit: true, prop.Name);
			if (TryUnwrapCollectionType(prop.Type, out var elemType) && bs == BoolSpecialKind.None
				&& TryParserTypeFqFromSymbol(prop) is null)
			{
				var isVariadicProp = isArg && prop.Type is IArrayTypeSymbol;
				return BuildCollectionParameterModel(prop.Type, elemType, prop, kind, cli, local, prop.Name,
					isSeparateType: true, defaultLiteral: null, doc.Description, meta,
					flagShortOpt: doc.ShortOpt, synopsisAliasesFromSummary: doc.Aliases, isVariadic: isVariadicProp,
					reportAcc: reportAcc,
					reportFallbackLocation: prop.Locations.FirstOrDefault() ?? reportFallbackLocation);
			}

			ClassifyScalarUnified(prop.Type, prop, bs, isSeparateType: true,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			if (reportAcc is not null)
				ReportFilesystemPathAttributeIssues(prop, sk, prop.Name, reportAcc,
					prop.Locations.FirstOrDefault() ?? reportFallbackLocation);

			var defaultValueLiteral = compilation is not null ? TryGetOptionsPropertyDefaultLiteral(prop, compilation) : null;
			// Cross-assembly [AsParameters] types: syntax refs empty, can't read initializer.
			var isCrossAssemblyDefault = defaultValueLiteral is null && prop.DeclaringSyntaxReferences.IsEmpty;
			var required = !isCrossAssemblyDefault && ComputeRequiredForOptionsType(prop.Type, bs) && defaultValueLiteral is null;
			var defLit = QualifyOptionsEnumDefaultLiteral(defaultValueLiteral, sk, enumFq, enumMembers);
			var enumCliNames = sk == CliScalarKind.Enum ? TryGetEnumCliNames(prop.Type) : default;
			var validations = ReadValidationConstraints(prop, sk, typeName);
			var expandProf = TryReadExpandUserProfileBeforeBind(prop, sk);
			var (isDeprecatedProp, deprecationMsgProp) = TryGetObsoleteAttribute(prop);
			var (isOutputProp, outputFormatsProp) = TryGetCommandOutputAttribute(prop);
			return new ParameterModel(
				prop.Name,
				local,
				cli,
				kind,
				bs,
				sk,
				typeName,
				enumFq,
				enumMembers,
				parserFq,
				customValFq,
				required,
				defLit,
				doc.Description,
				doc.ShortOpt,
				doc.Aliases,
				EnumMemberCliNames: enumCliNames,
				AsParametersOwnerParamName: methodParamName,
				AsParametersMemberOrder: memberOrder,
				AsParametersTypeFq: typeFq,
				AsParametersUseInit: true,
				AsParametersClrName: prop.Name,
				ExpandUserProfileBeforeBind: expandProf,
				Validations: validations,
				UsesRuntimeDefault: isCrossAssemblyDefault,
				IsNullableAnnotated: prop.Type.NullableAnnotation == NullableAnnotation.Annotated
					|| prop.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T },
				IsConfirmationSkip: HasConfirmationSkipAttribute(prop),
				IsDryRun: HasDryRunAttribute(prop),
				IsCommandOutput: isOutputProp,
				CommandOutputExplicitFormats: outputFormatsProp,
				IsDeprecated: isDeprecatedProp,
				DeprecationMessage: deprecationMsgProp);
		}

		private static bool ComputeRequiredForOptionsType(ITypeSymbol type, BoolSpecialKind bs)
		{
			if (bs == BoolSpecialKind.Bool)
				return false;

			if (type.NullableAnnotation == NullableAnnotation.Annotated)
				return false;

			if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
				return false;

			if (type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.Annotated)
				return true;

			return type.IsValueType && type.NullableAnnotation != NullableAnnotation.Annotated;
		}

		private static void ClassifyScalarForType(
			ITypeSymbol type,
			ISymbol attributeHost,
			BoolSpecialKind bs,
			out CliScalarKind kind,
			out string primitiveName,
			out string? enumFq,
			out ImmutableArray<string> enumMembers,
			out string? parserFq,
			out string? customValueFq)
		{
			enumFq = null;
			enumMembers = ImmutableArray<string>.Empty;
			parserFq = null;
			customValueFq = null;
			if (bs != BoolSpecialKind.None)
			{
				kind = CliScalarKind.Primitive;
				primitiveName = GetSimpleTypeName(type);
				return;
			}

			parserFq = TryParserTypeFqFromSymbol(attributeHost);
			if (parserFq is not null)
			{
				kind = CliScalarKind.CustomParser;
				primitiveName = "custom";
				customValueFq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				return;
			}

			var t = type;
			if (t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nn)
				t = nn.TypeArguments[0];

			if (t.TypeKind == TypeKind.Enum && t is INamedTypeSymbol en)
			{
				kind = CliScalarKind.Enum;
				enumFq = en.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				enumMembers = GetEnumMemberNames(en);
				primitiveName = "enum";
				return;
			}

			if (t is INamedTypeSymbol named)
			{
				var fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				if (fq == "global::System.IO.FileInfo")
				{
					kind = CliScalarKind.FileInfo;
					primitiveName = "FileInfo";
					return;
				}

				if (fq == "global::System.IO.DirectoryInfo")
				{
					kind = CliScalarKind.DirectoryInfo;
					primitiveName = "DirectoryInfo";
					return;
				}

				if (fq == "global::System.Uri")
				{
					kind = CliScalarKind.Uri;
					primitiveName = "Uri";
					return;
				}
			}

			kind = CliScalarKind.Primitive;
			primitiveName = GetSimpleTypeName(type);
		}

		private static string? TryParserTypeFqFromSymbol(ISymbol symbol)
		{
			foreach (var attr in symbol.GetAttributes())
			{
				if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
				    "global::Nullean.Argh.ArgumentParserAttribute")
					continue;

				if (attr.ConstructorArguments.Length > 0 &&
				    attr.ConstructorArguments[0].Value is INamedTypeSymbol parser)
					return parser.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			}

			return null;
		}

		private static void ClassifyScalar(
			IParameterSymbol p,
			BoolSpecialKind bs,
			out CliScalarKind kind,
			out string primitiveName,
			out string? enumFq,
			out ImmutableArray<string> enumMembers,
			out string? parserFq,
			out string? customValueFq)
		{
			enumFq = null;
			enumMembers = ImmutableArray<string>.Empty;
			parserFq = null;
			customValueFq = null;
			if (bs != BoolSpecialKind.None)
			{
				kind = CliScalarKind.Primitive;
				primitiveName = GetSimpleTypeName(p.Type);
				return;
			}

			parserFq = TryParserTypeFqFromSymbol(p);
			if (parserFq is not null)
			{
				kind = CliScalarKind.CustomParser;
				primitiveName = "custom";
				customValueFq = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				return;
			}

			var t = p.Type;
			if (t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nn)
				t = nn.TypeArguments[0];

			if (t.TypeKind == TypeKind.Enum && t is INamedTypeSymbol en)
			{
				kind = CliScalarKind.Enum;
				enumFq = en.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				enumMembers = GetEnumMemberNames(en);
				primitiveName = "enum";
				return;
			}

			if (t is INamedTypeSymbol named)
			{
				var fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				if (fq == "global::System.IO.FileInfo")
				{
					kind = CliScalarKind.FileInfo;
					primitiveName = "FileInfo";
					return;
				}

				if (fq == "global::System.IO.DirectoryInfo")
				{
					kind = CliScalarKind.DirectoryInfo;
					primitiveName = "DirectoryInfo";
					return;
				}

				if (fq == "global::System.Uri")
				{
					kind = CliScalarKind.Uri;
					primitiveName = "Uri";
					return;
				}
			}

			kind = CliScalarKind.Primitive;
			primitiveName = GetSimpleTypeName(p.Type);
		}

		private static ImmutableArray<string> GetEnumMemberNames(INamedTypeSymbol enumType)
		{
			var b = ImmutableArray.CreateBuilder<string>();
			foreach (var m in enumType.GetMembers())
			{
				if (m is IFieldSymbol { HasConstantValue: true, IsImplicitlyDeclared: false })
					b.Add(m.Name);
			}

			return b.ToImmutable();
		}

		private static ImmutableArray<string> GetEnumMemberCliNames(INamedTypeSymbol enumType)
		{
			var hasAny = false;
			var b = ImmutableArray.CreateBuilder<string>();
			foreach (var m in enumType.GetMembers())
			{
				if (m is not IFieldSymbol { HasConstantValue: true, IsImplicitlyDeclared: false })
					continue;
				string? cliName = null;
				foreach (var attr in m.GetAttributes())
				{
					if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Nullean.Argh.EnumValueAttribute"
					    && attr.ConstructorArguments.Length > 0
					    && attr.ConstructorArguments[0].Value is string v)
					{
						cliName = v;
						hasAny = true;
						break;
					}
				}
				b.Add(cliName ?? m.Name.ToLowerInvariant());
			}
			return hasAny ? b.ToImmutable() : default;
		}

		private static ImmutableArray<string> TryGetEnumCliNames(ITypeSymbol type)
		{
			var t = type;
			if (t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nn)
				t = nn.TypeArguments[0];
			return t is INamedTypeSymbol { TypeKind: TypeKind.Enum } en ? GetEnumMemberCliNames(en) : default;
		}

		private static ImmutableDictionary<string, string> GetEnumMemberDocs(INamedTypeSymbol enumType)
		{
			var b = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
			foreach (var m in enumType.GetMembers())
			{
				if (m is not IFieldSymbol { HasConstantValue: true, IsImplicitlyDeclared: false } field)
					continue;
				var xml = field.GetDocumentationCommentXml();
				if (string.IsNullOrWhiteSpace(xml))
					continue;
				try
				{
					var doc = System.Xml.Linq.XDocument.Parse("<root>" + xml + "</root>", System.Xml.Linq.LoadOptions.PreserveWhitespace);
					var summary = Documentation.FlattenBlockPublic(doc.Root?.Element("summary")).Replace("\r\n", "\n").Trim();
					if (!string.IsNullOrWhiteSpace(summary))
						b[field.Name] = summary;
				}
				catch { }
			}
			return b.ToImmutable();
		}

		private static bool IsInjectedStatic(IParameterSymbol p) => IsInjectedType(p.Type);

		private static BoolSpecialKind ClassifyBool(ITypeSymbol type)
		{
			if (type.SpecialType == SpecialType.System_Boolean)
				return BoolSpecialKind.Bool;

			if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named &&
			    named.TypeArguments[0].SpecialType == SpecialType.System_Boolean)
				return BoolSpecialKind.NullableBool;

			return BoolSpecialKind.None;
		}

		private static string GetSimpleTypeName(ITypeSymbol type)
		{
			if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nn)
			{
				var inner = GetSimpleTypeName(nn.TypeArguments[0]);
				if (inner == "bool")
					return "bool?";
				return inner + "?";
			}

			if (type.SpecialType == SpecialType.System_String)
				return "string";
			if (type.SpecialType == SpecialType.System_Int32)
				return "int";
			if (type.SpecialType == SpecialType.System_Int64)
				return "long";
			if (type.SpecialType == SpecialType.System_Single)
				return "float";
			if (type.SpecialType == SpecialType.System_Double)
				return "double";
			if (type.SpecialType == SpecialType.System_Decimal)
				return "decimal";
			if (type.SpecialType == SpecialType.System_Boolean)
				return "bool";

			if (type is INamedTypeSymbol named)
			{
				var fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				switch (fq)
				{
					case "global::System.DateTime":
						return "DateTime";
					case "global::System.DateTimeOffset":
						return "DateTimeOffset";
					case "global::System.TimeSpan":
						return "TimeSpan";
					case "global::System.DateOnly":
						return "DateOnly";
				}
			}

			return "string";
		}

		private static bool ComputeRequired(IParameterSymbol p, BoolSpecialKind bs)
		{
			if (bs == BoolSpecialKind.Bool)
				return false;

			if (p.HasExplicitDefaultValue)
				return false;

			if (p.Type.NullableAnnotation == NullableAnnotation.Annotated)
				return false;

			if (p.Type.IsReferenceType && p.Type.NullableAnnotation == NullableAnnotation.Annotated)
				return false;

			if (p.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
				return false;

			if (p.Type.IsReferenceType && p.Type.NullableAnnotation != NullableAnnotation.Annotated)
				return true;

			return p.Type.IsValueType && !p.HasExplicitDefaultValue && p.Type.NullableAnnotation != NullableAnnotation.Annotated;
		}

		private static string? TryGetDefaultLiteral(IParameterSymbol p, BoolSpecialKind bs)
		{
			if (bs == BoolSpecialKind.Bool)
				return "false";

			if (!p.HasExplicitDefaultValue)
				return null;

			var v = p.ExplicitDefaultValue;
			if (v is null)
				return p.Type.IsReferenceType ? "null" : "default";

			return v switch
			{
				string s => SymbolDisplay.FormatPrimitive(s, quoteStrings: true, useHexadecimalNumbers: false),
				char ch => SymbolDisplay.FormatPrimitive(ch, quoteStrings: true, useHexadecimalNumbers: false),
				bool b => b ? "true" : "false",
				IFormattable => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "default",
				_ => "default"
			};
		}

		private static string SafeLocalName(string name)
		{
			var k = Naming.ToCliLongName(name).Replace("-", "_");
			if (k.Length == 0)
				return "arg";
			if (!char.IsLetter(k[0]) && k[0] != '_')
				return "v_" + k;
			if (CSharpKeywords.Contains(k))
				return "@" + k;
			return k;
		}

		private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
		{
			"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
			"class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
			"enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
			"foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
			"long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
			"private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
			"sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
			"try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
			"void", "volatile", "while"
		};
	}

}
