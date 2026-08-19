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
	private static bool HasDefaultCommandAttribute(IMethodSymbol method)
	{
		foreach (var ad in method.GetAttributes())
		{
			if (ad.AttributeClass?.Name == "DefaultCommandAttribute" &&
			    ad.AttributeClass.ContainingNamespace?.ToDisplayString() == "Nullean.Argh")
				return true;
		}

		return false;
	}

	private static bool HasCommandIntrinsicAttribute(IMethodSymbol method)
	{
		foreach (var ad in method.GetAttributes())
		{
			if (ad.AttributeClass?.Name == "CommandIntrinsicAttribute" &&
			    ad.AttributeClass.ContainingNamespace?.ToDisplayString() == "Nullean.Argh")
				return true;
		}

		return false;
	}

	private static bool HasHiddenAttribute(ISymbol symbol)
	{
		foreach (var ad in symbol.GetAttributes())
		{
			if (ad.AttributeClass?.Name == "HiddenAttribute" &&
			    ad.AttributeClass.ContainingNamespace?.ToDisplayString() == "Nullean.Argh")
				return true;
		}

		return false;
	}

	private static string? TryGetCommandNameAttribute(IMethodSymbol method)
	{
		foreach (var ad in method.GetAttributes())
		{
			if (ad.AttributeClass?.Name == "CommandNameAttribute" &&
			    ad.AttributeClass.ContainingNamespace?.ToDisplayString() == "Nullean.Argh" &&
			    ad.ConstructorArguments.Length >= 1 &&
			    ad.ConstructorArguments[0].Value is string name &&
			    !string.IsNullOrWhiteSpace(name))
				return name;
		}

		return null;
	}

	private static ImmutableArray<string> TryGetCommandAliasesFromAttribute(IMethodSymbol method)
	{
		foreach (var ad in method.GetAttributes())
		{
			if (ad.AttributeClass?.Name == "CommandNameAttribute" &&
			    ad.AttributeClass.ContainingNamespace?.ToDisplayString() == "Nullean.Argh" &&
			    ad.ConstructorArguments.Length >= 2)
			{
				var aliasArg = ad.ConstructorArguments[1];
				if (aliasArg.Kind == TypedConstantKind.Array)
				{
					var builder = ImmutableArray.CreateBuilder<string>();
					foreach (var v in aliasArg.Values)
					{
						if (v.Value is string s && !string.IsNullOrWhiteSpace(s))
							builder.Add(s);
					}
					return builder.ToImmutable();
				}
			}
		}

		return ImmutableArray<string>.Empty;
	}

	private static (bool IsDeprecated, string? Message) TryGetObsoleteAttribute(ISymbol symbol)
	{
		foreach (var ad in symbol.GetAttributes())
		{
			if (ad.AttributeClass?.Name is "ObsoleteAttribute" or "Obsolete" &&
			    (ad.AttributeClass.ContainingNamespace?.ToDisplayString() is "System" or ""))
			{
				var msg = ad.ConstructorArguments.Length >= 1 && ad.ConstructorArguments[0].Value is string s && !string.IsNullOrWhiteSpace(s)
					? s
					: null;
				return (true, msg);
			}
		}

		return (false, null);
	}

	private const string DocNs = "Nullean.Argh.Documentation";

	private static CommandIntentData? TryGetCommandIntentData(IMethodSymbol method)
	{
		bool? destructive = null, idempotent = null, requiresConfirmation = null, requiresAuth = null;
		string? scope = null;

		foreach (var ad in method.GetAttributes())
		{
			if (ad.AttributeClass?.ContainingNamespace?.ToDisplayString() != DocNs) continue;

			switch (ad.AttributeClass.Name)
			{
				case "CommandIntentAttribute":
				{
					// Constructor arg 0 is the Intent flags enum (underlying int)
					// Destructive=1, Idempotent=2, RequiresConfirmation=4
					var flagsInt = 0;
					if (ad.ConstructorArguments.Length >= 1 && ad.ConstructorArguments[0].Value is int f)
						flagsInt = f;
					if ((flagsInt & 1) != 0) destructive = true;
					if ((flagsInt & 2) != 0) idempotent = true;
					if ((flagsInt & 4) != 0) requiresConfirmation = true;
					break;
				}
				case "MutationScopeAttribute":
				{
					// Constructor arg 0 is MutationScope enum: 0=File, 1=Directory, 2=Global
					if (ad.ConstructorArguments.Length >= 1 && ad.ConstructorArguments[0].Value is int s)
						scope = s switch { 0 => "file", 1 => "directory", 2 => "global", _ => null };
					break;
				}
				case "RequiresAuthAttribute":
					requiresAuth = true;
					break;
			}
		}

		if (destructive is null && idempotent is null && requiresConfirmation is null && requiresAuth is null && scope is null)
			return null;
		return new CommandIntentData(destructive, idempotent, scope, requiresConfirmation, requiresAuth);
	}

	private static (bool IsOutput, ImmutableArray<string> ExplicitFormats) TryGetCommandOutputAttribute(ISymbol symbol)
	{
		foreach (var ad in symbol.GetAttributes())
		{
			if (ad.AttributeClass?.Name == "CommandOutputAttribute" &&
			    ad.AttributeClass.ContainingNamespace?.ToDisplayString() == DocNs)
			{
				var formats = ImmutableArray<string>.Empty;
				if (ad.ConstructorArguments.Length >= 1 && ad.ConstructorArguments[0].Kind == TypedConstantKind.Array)
				{
					var builder = ImmutableArray.CreateBuilder<string>();
					foreach (var v in ad.ConstructorArguments[0].Values)
					{
						if (v.Value is string s && !string.IsNullOrWhiteSpace(s))
							builder.Add(s);
					}
					formats = builder.ToImmutable();
				}
				return (true, formats);
			}
		}
		return (false, ImmutableArray<string>.Empty);
	}

	private static bool HasConfirmationSkipAttribute(ISymbol symbol)
	{
		foreach (var ad in symbol.GetAttributes())
		{
			if (ad.AttributeClass?.Name == "ConfirmationSkipAttribute" &&
			    ad.AttributeClass.ContainingNamespace?.ToDisplayString() == DocNs)
				return true;
		}
		return false;
	}

	private static bool HasDryRunAttribute(ISymbol symbol)
	{
		foreach (var ad in symbol.GetAttributes())
		{
			if (ad.AttributeClass?.Name == "DryRunAttribute" &&
			    ad.AttributeClass.ContainingNamespace?.ToDisplayString() == DocNs)
				return true;
		}
		return false;
	}

	private static CommandOutputData? BuildCommandOutputFromParameters(ImmutableArray<ParameterModel> parameters)
	{
		foreach (var p in parameters)
		{
			if (!p.IsCommandOutput) continue;
			var flagName = "--" + p.CliLongName;
			ImmutableArray<string> formats;
			if (!p.CommandOutputExplicitFormats.IsDefaultOrEmpty)
				formats = p.CommandOutputExplicitFormats;
			else if (p.ScalarKind == CliScalarKind.Enum && !p.EnumMemberNames.IsDefaultOrEmpty)
			{
				// Resolve CLI names the same way the help/schema emitter does
				var builder = ImmutableArray.CreateBuilder<string>(p.EnumMemberNames.Length);
				for (var i = 0; i < p.EnumMemberNames.Length; i++)
					builder.Add(ResolveEnumMemberCliName(p.EnumMemberCliNames, i, p.EnumMemberNames[i]));
				formats = builder.ToImmutable();
			}
			else
				formats = ImmutableArray<string>.Empty;
			return new CommandOutputData(formats, flagName);
		}
		return null;
	}

	private static AIDocumentEnvironmentVariables? AnalyzeDocumentEnvironmentVariables(
		InvocationExpressionSyntax invocation, string filePath, int spanStart)
	{
		var varsBuilder = ImmutableArray.CreateBuilder<EnvVarDocEntry>();
		var cfgBuilder = ImmutableArray.CreateBuilder<ConfigFileDocEntry>();

		foreach (var arg in invocation.ArgumentList.Arguments)
		{
			var nameColon = arg.NameColon?.Name.Identifier.Text;

			if (arg.Expression is not (ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
				continue;

			ArgumentListSyntax? ctorArgs = arg.Expression switch
			{
				ObjectCreationExpressionSyntax o => o.ArgumentList,
				ImplicitObjectCreationExpressionSyntax i => i.ArgumentList,
				_ => null
			};
			if (ctorArgs is null) continue;

			// Determine type from name colon or array element pattern
			var typeName = arg.Expression is ObjectCreationExpressionSyntax oce
				? oce.Type.ToString()
				: null;
			bool isConfigFile = typeName?.Contains("ConfigFile") == true || nameColon == "configFiles";

			if (isConfigFile)
			{
				var path = ctorArgs.Arguments.Count >= 1
					? TryGetStringLiteral(ctorArgs.Arguments[0].Expression)
					: null;
				if (path is null) continue;
				string? desc = null;
				bool req = false;
				foreach (var ca in ctorArgs.Arguments)
				{
					var n = ca.NameColon?.Name.Identifier.Text;
					if (n == "Description") desc = TryGetStringLiteral(ca.Expression);
					if (n == "Required") req = TryGetBoolLiteral(ca.Expression) ?? false;
				}
				cfgBuilder.Add(new ConfigFileDocEntry(path, desc, req));
			}
			else
			{
				var name = ctorArgs.Arguments.Count >= 1
					? TryGetStringLiteral(ctorArgs.Arguments[0].Expression)
					: null;
				if (name is null) continue;
				string? desc = null;
				bool req = false;
				string? defVal = null;
				foreach (var ca in ctorArgs.Arguments)
				{
					var n = ca.NameColon?.Name.Identifier.Text;
					if (n == "Description") desc = TryGetStringLiteral(ca.Expression);
					if (n == "Required") req = TryGetBoolLiteral(ca.Expression) ?? false;
					if (n == "DefaultValue") defVal = TryGetStringLiteral(ca.Expression);
				}
				varsBuilder.Add(new EnvVarDocEntry(name, desc, req, defVal));
			}
		}

		if (varsBuilder.Count == 0 && cfgBuilder.Count == 0) return null;
		return new AIDocumentEnvironmentVariables(filePath, spanStart, varsBuilder.ToImmutable(), cfgBuilder.ToImmutable());
	}

	private static bool? TryGetBoolLiteral(ExpressionSyntax expr) =>
		expr.Kind() switch
		{
			Microsoft.CodeAnalysis.CSharp.SyntaxKind.TrueLiteralExpression => true,
			Microsoft.CodeAnalysis.CSharp.SyntaxKind.FalseLiteralExpression => false,
			_ => null
		};

	private static ExpressionSyntax? TryGetPropertyInitializerValueSyntax(IPropertySymbol prop)
	{
		foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
		{
			if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax { Initializer: { Value: var expr } })
				return expr;
		}

		return null;
	}

	private static ExpressionSyntax? TryGetFieldInitializerValueSyntax(IFieldSymbol field)
	{
		foreach (var syntaxRef in field.DeclaringSyntaxReferences)
		{
			if (syntaxRef.GetSyntax() is VariableDeclaratorSyntax { Initializer: { Value: var expr } })
				return expr;
		}

		return null;
	}

	/// <summary>
	/// Some initializer shapes yield a bare enum member name (e.g. <c>Information</c>). Emit must use a type-qualified form.
	/// </summary>
	private static string? QualifyOptionsEnumDefaultLiteral(
		string? literal,
		CliScalarKind sk,
		string? enumFq,
		ImmutableArray<string> enumMembers)
	{
		if (literal is null || sk != CliScalarKind.Enum || string.IsNullOrEmpty(enumFq))
			return literal;
		if (literal.StartsWith("global::", StringComparison.Ordinal) || literal.StartsWith("(", StringComparison.Ordinal))
			return literal;
		if (literal.Contains("::", StringComparison.Ordinal))
			return literal;
		foreach (var m in enumMembers)
		{
			if (!string.Equals(m, literal, StringComparison.Ordinal))
				continue;
			return enumFq + "." + literal;
		}

		return literal;
	}

	private static bool EnumConstantValuesEqual(object fieldConst, object literalConst)
	{
		if (Equals(fieldConst, literalConst))
			return true;
		try
		{
			return Convert.ToDecimal(fieldConst, CultureInfo.InvariantCulture) ==
			       Convert.ToDecimal(literalConst, CultureInfo.InvariantCulture);
		}
		catch
		{
			return false;
		}
	}

	private static string? TryFormatInitializerOperation(IOperation? op, INamedTypeSymbol? enumTypeHint = null)
	{
		while (op is IConversionOperation conv)
			op = conv.Operand;
		while (op is IParenthesizedOperation paren)
			op = paren.Operand;

		switch (op)
		{
			case IFieldReferenceOperation { Field: var ef } when ef.IsStatic && (ef.HasConstantValue || ef.ContainingType?.TypeKind == TypeKind.Enum):
				return ef.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			case ILiteralOperation lit when lit.ConstantValue.HasValue && lit.ConstantValue.Value is { } v:
			{
				var enm = lit.Type is INamedTypeSymbol litEnum && litEnum.TypeKind == TypeKind.Enum
					? litEnum
					: enumTypeHint is { TypeKind: TypeKind.Enum } hintEnum
						? hintEnum
						: null;
				if (enm is not null)
				{
					foreach (var m in enm.GetMembers())
					{
						if (m is not IFieldSymbol fld || !fld.HasConstantValue)
							continue;
						if (EnumConstantValuesEqual(fld.ConstantValue, lit.ConstantValue.Value))
							return fld.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
					}

					return null;
				}

				return v switch
				{
					string s => SymbolDisplay.FormatPrimitive(s, quoteStrings: true, useHexadecimalNumbers: false),
					char ch => SymbolDisplay.FormatPrimitive(ch, quoteStrings: true, useHexadecimalNumbers: false),
					bool b => b ? "true" : "false",
					IFormattable => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "default",
					_ => "default"
				};
			}
			default:
				return null;
		}
	}

	/// <summary>
	/// Resolves a <see cref="SemanticModel"/> for <paramref name="tree"/> even when it does not belong to
	/// <paramref name="compilation"/> directly. In multi-project solution builds (e.g. Rider/VS design-time
	/// builds, which use <see cref="CompilationReference"/> instead of metadata for ProjectReferences), a
	/// symbol's <c>DeclaringSyntaxReferences</c> can point at a syntax tree that only lives in a *referenced*
	/// project's compilation. Calling <c>compilation.GetSemanticModel</c> on such a tree throws
	/// <see cref="ArgumentException"/> ("SyntaxTree is not part of the compilation"). We walk compilation
	/// references to find the compilation that actually owns the tree, and return <c>null</c> if none does
	/// (e.g. plain metadata references) so callers can degrade gracefully instead of crashing the generator.
	/// </summary>
	private static SemanticModel? TryGetSemanticModelForSyntaxTree(Compilation compilation, SyntaxTree tree)
	{
		if (compilation.ContainsSyntaxTree(tree))
			return compilation.GetSemanticModel(tree);

		foreach (var reference in compilation.References)
		{
			if (reference is not CompilationReference compilationReference)
				continue;
			var model = TryGetSemanticModelForSyntaxTree(compilationReference.Compilation, tree);
			if (model is not null)
				return model;
		}

		return null;
	}

	private static string? TryFormatOptionsInitializerExpression(Compilation compilation, ExpressionSyntax expr, ITypeSymbol? enumTypeHint = null)
	{
		var model = TryGetSemanticModelForSyntaxTree(compilation, expr.SyntaxTree);
		if (model is null)
			return null;
		var hint = enumTypeHint is INamedTypeSymbol namedHint && namedHint.TypeKind == TypeKind.Enum ? namedHint : null;
		var fromOp = TryFormatInitializerOperation(model.GetOperation(expr), hint);
		if (fromOp is not null)
			return fromOp;

		// Fallback when IOperation shape is unexpected (e.g. some enum constant shapes in property initializers).
		var sym = model.GetSymbolInfo(expr).Symbol;
		if (sym is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } ef)
			return ef.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		return null;
	}

	private static string? TryGetOptionsPropertyDefaultLiteral(IPropertySymbol prop, Compilation compilation) =>
		TryGetPropertyInitializerValueSyntax(prop) is { } expr ? TryFormatOptionsInitializerExpression(compilation, expr, prop.Type) : null;

	private static string? TryGetOptionsFieldDefaultLiteral(IFieldSymbol field, Compilation compilation) =>
		TryGetFieldInitializerValueSyntax(field) is { } expr ? TryFormatOptionsInitializerExpression(compilation, expr, field.Type) : null;

	private static OptionsTypeModel? BuildOptionsTypeModel(INamedTypeSymbol type, Compilation compilation)
	{
		var members = ImmutableArray.CreateBuilder<ParameterModel>();
		foreach (var member in type.GetMembers())
		{
			switch (member)
			{
				case IPropertySymbol prop when prop.DeclaredAccessibility == Accessibility.Public && !prop.IsStatic:
				{
					if (prop.IsIndexer)
						continue;
					if (prop.GetMethod is null || prop.SetMethod is null)
						continue;
					members.Add(ParameterModel.FromOptionsProperty(prop, compilation, TryGetOptionsPropertyDefaultLiteral(prop, compilation)));
					break;
				}
				case IFieldSymbol field when field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic:
					members.Add(ParameterModel.FromOptionsField(field, compilation, TryGetOptionsFieldDefaultLiteral(field, compilation)));
					break;
			}
		}

		var typeFq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var typeMetaName = GetMetadataNameStatic(type);
		var baseNames = CollectBaseTypeMetadataNames(type);
		var flattenedMembers = BuildFlattenedOptionsMembers(type, compilation);
		var bestCtorParamOrder = ComputeBestCtorParamOrder(type, members.Count > 0 ? members.ToImmutable() : ImmutableArray<ParameterModel>.Empty);
		var isPublic = type.DeclaredAccessibility == Accessibility.Public;
		var isGeneric = type.TypeParameters.Length > 0;

		if (members.Count == 0)
			return new OptionsTypeModel(typeFq, typeMetaName, baseNames, ImmutableArray<ParameterModel>.Empty, flattenedMembers, bestCtorParamOrder, isPublic, isGeneric);

		return new OptionsTypeModel(typeFq, typeMetaName, baseNames, members.ToImmutable(), flattenedMembers, bestCtorParamOrder, isPublic, isGeneric);
	}

	private static ImmutableArray<ParameterModel> BuildFlattenedOptionsMembers(INamedTypeSymbol type, Compilation compilation)
	{
		var chain = new List<INamedTypeSymbol>();
		for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
			chain.Add(t);

		var members = ImmutableArray.CreateBuilder<ParameterModel>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = chain.Count - 1; i >= 0; i--)
		{
			var tt = chain[i];
			foreach (var member in tt.GetMembers())
			{
				switch (member)
				{
					case IPropertySymbol prop when prop.DeclaredAccessibility == Accessibility.Public && !prop.IsStatic:
					{
						if (prop.IsIndexer)
							continue;
						if (prop.GetMethod is null || prop.SetMethod is null)
							continue;
						if (!seen.Add(prop.Name))
							continue;

						members.Add(ParameterModel.FromOptionsProperty(prop, compilation, TryGetOptionsPropertyDefaultLiteral(prop, compilation)));
						break;
					}
					case IFieldSymbol field when field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic:
					{
						if (!seen.Add(field.Name))
							continue;

						members.Add(ParameterModel.FromOptionsField(field, compilation, TryGetOptionsFieldDefaultLiteral(field, compilation)));
						break;
					}
				}
			}
		}

		return members.ToImmutable();
	}

	/// <summary>Pre-computes the parameter name order for the best public non-empty constructor (for symbol-free emit).</summary>
	private static ImmutableArray<string>? ComputeBestCtorParamOrder(INamedTypeSymbol type, ImmutableArray<ParameterModel> members)
	{
		if (members.IsDefaultOrEmpty)
			return null;
		var byName = new HashSet<string>(members.Select(m => m.SymbolName), StringComparer.OrdinalIgnoreCase);
		IMethodSymbol? bestCtor = null;
		foreach (var ctor in type.InstanceConstructors)
		{
			if (ctor.DeclaredAccessibility != Accessibility.Public) continue;
			if (ctor.Parameters.Length == 0) continue;
			if (!ctor.Parameters.All(p => byName.Contains(p.Name))) continue;
			if (bestCtor is null || ctor.Parameters.Length > bestCtor.Parameters.Length)
				bestCtor = ctor;
		}
		if (bestCtor is null || bestCtor.Parameters.Length != members.Length)
			return null;
		var b = ImmutableArray.CreateBuilder<string>(bestCtor.Parameters.Length);
		foreach (var p in bestCtor.Parameters)
			b.Add(p.Name);
		return b.MoveToImmutable();
	}

	private static string GetMetadataNameStatic(ITypeSymbol t) =>
		t.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

	private static ImmutableArray<string> CollectBaseTypeMetadataNames(INamedTypeSymbol type)
	{
		var b = ImmutableArray.CreateBuilder<string>();
		var current = type.BaseType;
		while (current is not null && current.SpecialType != SpecialType.System_Object)
		{
			b.Add(GetMetadataNameStatic(current));
			current = current.BaseType;
		}
		foreach (var iface in type.AllInterfaces)
			b.Add(GetMetadataNameStatic(iface));
		return b.ToImmutable();
	}



	private static bool IsInjectedType(ITypeSymbol type)
	{
		if (type is INamedTypeSymbol named && named.TypeKind == TypeKind.Struct)
		{
			var fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			return fq == "global::System.Threading.CancellationToken";
		}

		return false;
	}

	private static bool IsInjected(IParameterSymbol p) => IsInjectedType(p.Type);

	private static bool HasArgumentAttribute(IParameterSymbol p)
	{
		foreach (var attr in p.GetAttributes())
		{
			if (attr.AttributeClass?.Name == "ArgumentAttribute")
				return true;
		}

		return false;
	}

	private static bool HasArgumentAttribute(IPropertySymbol p)
	{
		foreach (var attr in p.GetAttributes())
		{
			if (attr.AttributeClass?.Name == "ArgumentAttribute")
				return true;
		}

		return false;
	}

	private static bool HasAsParametersAttribute(IParameterSymbol p)
	{
		foreach (var attr in p.GetAttributes())
		{
			if (attr.AttributeClass?.Name == "AsParametersAttribute")
				return true;
		}

		return false;
	}

	private static string? GetAsParametersPrefix(IParameterSymbol p)
	{
		foreach (var attr in p.GetAttributes())
		{
			if (attr.AttributeClass?.Name != "AsParametersAttribute")
				continue;
			if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string s && !string.IsNullOrWhiteSpace(s))
				return s.Trim();
		}

		return null;
	}

	private static bool TryUnwrapCollectionType(ITypeSymbol type, out ITypeSymbol elementType)
	{
		elementType = null!;
		switch (type)
		{
			case IArrayTypeSymbol arr:
				elementType = arr.ElementType;
				return true;
			case INamedTypeSymbol named:
			{
				var def = named.OriginalDefinition;
				var fq = def.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				if (fq is "global::System.Collections.Generic.IEnumerable<T>"
				    or "global::System.Collections.Generic.IReadOnlyList<T>"
				    or "global::System.Collections.Generic.IReadOnlySet<T>"
				    or "global::System.Collections.Generic.List<T>")
				{
					if (named.TypeArguments.Length == 1)
					{
						elementType = named.TypeArguments[0];
						return true;
					}
				}

				return false;
			}
			default:
				return false;
		}
	}

	private static string? TryGetCollectionSeparatorFromAttribute(ISymbol symbol)
	{
		var fromSymbol = TryGetCollectionSeparatorFromSymbol(symbol);
		if (fromSymbol is not null)
			return fromSymbol;

		// Positional record members can target [CollectionSyntax] at the synthesized property
		// ([property: ...]) instead of the constructor parameter ([param: ...]).
		if (symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } ctor } ctorParam)
		{
			var mirroredProperty = TryFindCtorMirroredProperty(ctor.ContainingType, ctorParam.Name);
			if (mirroredProperty is not null)
				return TryGetCollectionSeparatorFromSymbol(mirroredProperty);
		}

		return null;
	}

	private static string? TryGetCollectionSeparatorFromSymbol(ISymbol symbol)
	{
		foreach (var attr in symbol.GetAttributes())
		{
			if (attr.AttributeClass?.Name != "CollectionSyntaxAttribute")
				continue;
			foreach (var na in attr.NamedArguments)
			{
				if (na.Key == "Separator" && na.Value.Value is string s && s.Length > 0)
					return s;
			}
		}

		return null;
	}

	private static IPropertySymbol? TryFindCtorMirroredProperty(INamedTypeSymbol type, string ctorParameterName)
	{
		foreach (var member in type.GetMembers())
		{
			if (member is not IPropertySymbol prop)
				continue;
			if (!string.Equals(prop.Name, ctorParameterName, StringComparison.OrdinalIgnoreCase))
				continue;
			return prop;
		}

		return null;
	}

	private static IMethodSymbol? TryGetPrimaryConstructor(INamedTypeSymbol type)
	{
		IMethodSymbol? best = null;
		foreach (var m in type.GetMembers())
		{
			if (m is not IMethodSymbol { MethodKind: MethodKind.Constructor } ctor)
				continue;
			if (ctor.IsStatic)
				continue;
			if (ctor.DeclaredAccessibility != Accessibility.Public)
				continue;
			if (best is null || ctor.Parameters.Length > best.Parameters.Length)
				best = ctor;
		}

		return best;
	}

	private static bool IsInitOnlySettable(IPropertySymbol prop)
	{
		if (prop.IsStatic)
			return false;
		if (prop.GetMethod is null)
			return false;
		var set = prop.SetMethod;
		if (set is null)
			return false;
		return set.IsInitOnly;
	}

	/// <summary>Properties eligible for [AsParameters] object-initializer binding (init or normal setter).</summary>
	private static bool IsSettableForAsParameters(IPropertySymbol prop)
	{
		if (prop.IsStatic)
			return false;
		if (prop.GetMethod is null)
			return false;
		return prop.SetMethod is not null;
	}


}
