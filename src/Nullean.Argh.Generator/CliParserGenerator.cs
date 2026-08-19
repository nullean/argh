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

[Generator]
public sealed partial class CliParserGenerator : IIncrementalGenerator
{
	private const string ArghAppMetadataName = "Nullean.Argh.ArghApp";
	private const string IArghBuilderMetadataName = "Nullean.Argh.Builder.IArghBuilder";
	private const string ArghBuilderMetadataName = "Nullean.Argh.Builder.ArghBuilder";
	private const string IArghNamespaceBuilderMetadataName = "Nullean.Argh.Builder.IArghNamespaceBuilder";
	private const string ArghNamespaceBuilderMetadataName = "Nullean.Argh.Builder.ArghNamespaceBuilder";

	/// <summary>
	/// Same as <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>, plus NRT modifiers so emitted collection locals match nullable annotations (e.g. <c>IReadOnlySet&lt;int&gt;?</c> for unset optional collections).
	/// </summary>
	private static readonly SymbolDisplayFormat FullyQualifiedFormatWithNullableRefAnnotations =
		SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
			SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
			SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

	// ── Pre-compiled Regex patterns ── compiled once, reused for every handler method analyzed
	private static readonly Regex SummaryXmlPattern =
		new(@"<summary>\s*([\s\S]*?)\s*</summary>", RegexOptions.Compiled);
	private static readonly Regex RemarksXmlPattern =
		new(@"<remarks>\s*([\s\S]*?)\s*</remarks>", RegexOptions.Compiled);
	private static readonly Regex DocTriviaStripPattern =
		new(@"^\s*///\s?", RegexOptions.Compiled | RegexOptions.Multiline);
	private static readonly Regex WhitespaceCollapsePattern =
		new(@"\s+", RegexOptions.Compiled);
	private static readonly Regex IdentifierSegmentPattern =
		new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

	/// <summary>Syntax-only filter for Argh builder invocations; <c>Map</c> is narrowed to generic or two-arg forms.</summary>
	private static bool IsTrackedArghInvocation(InvocationExpressionSyntax inv, MemberAccessExpressionSyntax ma, string methodName)
	{
		switch (methodName)
		{
			case "UseCliDescription":
			case "UseSchemaVersion":
			case "DocumentEnvironmentVariables":
			case "MapNamespace":
			case "MapRoot":
			case "UseGlobalOptions":
			case "UseNamespaceOptions":
			case "UseMiddleware":
				return true;
			case "Map":
				if (ma.Name is GenericNameSyntax gn && gn.TypeArgumentList.Arguments.Count > 0)
					return true;
				return inv.ArgumentList.Arguments.Count >= 2;
			case "MapAndRootAlias":
				return ma.Name is GenericNameSyntax gna && gna.TypeArgumentList.Arguments.Count > 0;
			default:
				return false;
		}
	}

	private static string GetEntryAssemblyMajorVersion(Compilation compilation) =>
		(compilation.Assembly.Identity.Version?.Major ?? 0).ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// Version string for <c>--version</c> and CLI schema: prefer
	/// <see cref="AssemblyInformationalVersionAttribute"/> (semantic / MinVer output), then assembly identity version.
	/// </summary>
	private static string GetEntryAssemblyDisplayVersion(Compilation compilation)
	{
		var informationalAttr = compilation.GetTypeByMetadataName(
			"System.Reflection.AssemblyInformationalVersionAttribute");
		foreach (var attribute in compilation.Assembly.GetAttributes())
		{
			if (attribute.AttributeClass is not { } attributeClass)
				continue;
			if (informationalAttr is not null
			    && !SymbolEqualityComparer.Default.Equals(attributeClass, informationalAttr)
			    && attributeClass.Name is not ("AssemblyInformationalVersionAttribute" or "AssemblyInformationalVersion"))
				continue;
			if (informationalAttr is null
			    && attributeClass.Name is not ("AssemblyInformationalVersionAttribute" or "AssemblyInformationalVersion"))
				continue;
			if (attribute.ConstructorArguments.Length > 0
			    && attribute.ConstructorArguments[0].Value is string informational
			    && !string.IsNullOrWhiteSpace(informational))
				return informational;
		}

		return compilation.Assembly.Identity.Version?.ToString() ?? "0.0.0.0";
	}

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		// ── Assembly metadata ── stable, only changes on version bump
		var assemblyInfo = context.CompilationProvider
			.Select(static (c, _) => (
				Name: c.Assembly.Identity.Name ?? "app",
				Ver: GetEntryAssemblyDisplayVersion(c),
				SchemaVer: GetEntryAssemblyMajorVersion(c)));

		// ── Parse options ── changes only when LangVersion/nullable/defines change
		var parseOpts = context.ParseOptionsProvider
			.Select(static (o, _) => o as CSharpParseOptions ?? CSharpParseOptions.Default);

		// ── Capabilities from metadata references ──
		var capabilities = context.MetadataReferencesProvider
			.Collect()
			.Select(static (refs, _) => ReferenceMetadataCapabilities.Compute(refs));

		// ── Artifacts layout — used for robust XML doc resolution from cross-assembly references ──
		// ArtifactsPath (from <ArtifactsPath>) is exposed via CompilerVisibleProperty in
		// build/Nullean.Argh.Core.props so that the generator can resolve companion XML documentation
		// files for types in projects that use <UseArtifactsOutput>true</UseArtifactsOutput>.
		var artifactsPath = context.AnalyzerConfigOptionsProvider
			.Select(static (opts, _) =>
			{
				opts.GlobalOptions.TryGetValue("build_property.ArtifactsPath", out var path);
				return string.IsNullOrWhiteSpace(path) ? null : path!.Trim();
			});

		// ── Per-invocation semantic analysis — cached per-invocation by Roslyn ──
		// Each invocation is analyzed independently and produces a symbol-free AnalyzedInvocation.
		// Roslyn caches the result per invocation; only changed invocations are re-analyzed.
		var analyzed = context.SyntaxProvider
			.CreateSyntaxProvider(
				static (node, _) =>
				{
					if (node is not InvocationExpressionSyntax inv
					    || inv.Expression is not MemberAccessExpressionSyntax ma)
						return false;
					var methodName = ma.Name switch
					{
						GenericNameSyntax gn => gn.Identifier.Text,
						SimpleNameSyntax sn => sn.Identifier.Text,
						_ => (string?)null
					};
					return methodName is not null && IsTrackedArghInvocation(inv, ma, methodName);
				},
				static (ctx, ct) =>
				{
					var invocation = (InvocationExpressionSyntax)ctx.Node;
					if (invocation.Expression is not MemberAccessExpressionSyntax member)
						return null;
					var receiverType = ctx.SemanticModel.GetTypeInfo(member.Expression, ct).Type;
					if (receiverType is null)
						return null;
					// Quick namespace filter — only Nullean.Argh types
					if (!IsArghNamespace(receiverType))
					{
						if (receiverType is not INamedTypeSymbol named2)
							return null;
						var isArgh = false;
						foreach (var iface in named2.AllInterfaces)
						{
							if (IsArghNamespace(iface)) { isArgh = true; break; }
						}
						if (!isArgh) return null;
					}
					return AnalyzeInvocation(invocation, ctx.SemanticModel, ct);

					static bool IsArghNamespace(ITypeSymbol t) =>
						t.ContainingNamespace?.ToDisplayString().StartsWith("Nullean.Argh", StringComparison.Ordinal) == true;
				})
			.Where(x => x is not null)
			.Select(static (x, _) => x!)
			.Collect();

		var combined = analyzed
			.Combine(assemblyInfo)
			.Combine(capabilities)
			.Combine(parseOpts)
			.Combine(artifactsPath);

		context.RegisterSourceOutput(combined, static (spc, tuple) =>
		{
			var ((((analyzedArray, (asmName, asmVer, asmSchemaVer)), caps), po), artifactsPathValue) = tuple;
			Execute(spc, analyzedArray, asmName, asmVer, asmSchemaVer, caps, po, artifactsPathValue);
		});
	}


	/// <summary>New Execute — fully incremental: no Compilation reference, works with symbol-free AnalyzedInvocation[].</summary>
	private static void Execute(
		SourceProductionContext context,
		ImmutableArray<AnalyzedInvocation> analyzed,
		string entryAsmName,
		string entryAsmVersion,
		string entrySchemaVersion,
		ReferenceMetadataCapabilities.Capabilities referenceCapabilities,
		CSharpParseOptions parseOpts,
		string? artifactsPath = null)
	{
		if (analyzed.IsDefaultOrEmpty)
		{
			EmitEmpty(context, entryAsmName, entryAsmVersion);
			return;
		}

		var built = TryBuildAppEmitModel(context, analyzed, out var appModel);
		if (appModel is not null)
			EmitNamespaceSegmentCodegen(context, appModel);

		if (!built || appModel is null)
		{
			EmitEmpty(context, entryAsmName, entryAsmVersion);
			return;
		}

		EmitApp(context, appModel, parseOpts, entryAsmName, entryAsmVersion, entrySchemaVersion, referenceCapabilities);
	}

	/// <summary>Legacy Execute — kept for reference / fallback; not wired into the pipeline.</summary>

	private static ITypeSymbol? GetReceiverType(SemanticModel model, InvocationExpressionSyntax invocation)
	{
		if (invocation.Expression is not MemberAccessExpressionSyntax member)
			return null;

		return model.GetTypeInfo(member.Expression).Type;
	}

	private static bool IsArghRegistrationReceiver(
		ITypeSymbol receiver,
		INamedTypeSymbol arghApp,
		INamedTypeSymbol? iArghBuilder,
		INamedTypeSymbol? arghBuilderType,
		INamedTypeSymbol? iArghNamespaceBuilder,
		INamedTypeSymbol? arghNamespaceBuilderType)
	{
		if (SymbolEqualityComparer.Default.Equals(receiver, arghApp))
			return true;

		if (iArghBuilder is not null && SymbolEqualityComparer.Default.Equals(receiver, iArghBuilder))
			return true;

		if (arghBuilderType is not null && SymbolEqualityComparer.Default.Equals(receiver, arghBuilderType))
			return true;

		if (iArghNamespaceBuilder is not null && SymbolEqualityComparer.Default.Equals(receiver, iArghNamespaceBuilder))
			return true;

		if (arghNamespaceBuilderType is not null && SymbolEqualityComparer.Default.Equals(receiver, arghNamespaceBuilderType))
			return true;

		if (receiver is INamedTypeSymbol named)
		{
			if (iArghBuilder is not null)
			{
				foreach (var iface in named.AllInterfaces)
				{
					if (SymbolEqualityComparer.Default.Equals(iface, iArghBuilder))
						return true;
				}
			}

			if (iArghNamespaceBuilder is not null)
			{
				foreach (var iface in named.AllInterfaces)
				{
					if (SymbolEqualityComparer.Default.Equals(iface, iArghNamespaceBuilder))
						return true;
				}
			}
		}

		return false;
	}

}
