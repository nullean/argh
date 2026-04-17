using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
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

	private static readonly DiagnosticDescriptor CommandNamespaceOptionsMustExtendParent = new(
		"AGH0004",
		"Command namespace options type must extend the parent options type",
		"'{0}' must inherit or implement '{1}' for this CommandNamespaceOptions<> registration.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor CommandNamespaceOptionsRequiresParent = new(
		"AGH0005",
		"Command namespace options require a parent options type",
		"Register GlobalOptions<T>() before CommandNamespaceOptions<{0}>(), or ensure the parent namespace declares a compatible base options type.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor HandlerMustBeMethod = new(
		"AGH0002",
		"Command handler must be a method group",
		"The second argument to Add must be a method group (not a lambda or local function) so the generator can emit an AOT-compatible call.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor ArgumentOrder = new(
		"AGH0003",
		"Invalid [Argument] parameter order",
		"Parameters marked with [Argument] must start at position 0 and be consecutive.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor UseMiddlewareDelegateNotSupported = new(
		"AGH0006",
		"Inline UseMiddleware delegate not emitted",
		"UseMiddleware requires a type argument (UseMiddleware<T>()) for source-generated middleware; inline delegates are not emitted.",
		"Argh",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor DuplicateCliNames = new(
		"AGH0007",
		"Duplicate CLI names",
		"Multiple parameters map to the same CLI name '{0}' (conflicts when binding or generating help).",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor CollectionPositionalNotSupported = new(
		"AGH0008",
		"Collection parameters must be flags",
		"Collection types are only supported for option flags, not for [Argument] positionals.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor AsParametersEmptyType = new(
		"AGH0009",
		"AsParameters type has no bindable members",
		"Type '{0}' must expose public primary constructor parameters and/or public settable properties (including inherited) for [AsParameters] binding.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor DuplicateRootCommand = new(
		"AGH0010",
		"Duplicate default command",
		"Only one default handler per scope: AddRootCommand, AddNamespaceRootCommand, or [DefaultCommand].",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor AddRootCommandOnlyAtAppRoot = new(
		"AGH0011",
		"AddRootCommand only on the root app",
		"Use AddRootCommand on the root ArghApp only (not inside AddNamespace). For a namespace default handler, use AddNamespaceRootCommand.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor AddNamespaceRootCommandOnlyInNamespace = new(
		"AGH0012",
		"AddNamespaceRootCommand only inside a namespace",
		"Use AddNamespaceRootCommand inside AddNamespace configuration. For the top-level default, use AddRootCommand.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor ReservedCommandNameRoot = new(
		"AGH0013",
		"Reserved command name",
		"The name '{0}' is reserved for root default commands; choose a different command name.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor AddNamespaceRequiresExplicitDescriptionOrType = new(
		"AGH0014",
		"AddNamespace requires a description or entry type",
		"Use AddNamespace(string name, string description, Action<IArghBuilder> configure) with an explicit description (may be empty), or AddNamespace<T>(string name, Action<IArghBuilder> configure) to use type T's XML summary for the namespace listing.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor AddNamespaceDescriptionNotConstant = new(
		"AGH0015",
		"AddNamespace description not a compile-time string",
		"The description argument must be a string literal or const string so the generator can emit namespace help text.",
		"Argh",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor RedundantAddInsideAddNamespaceT = new(
		"AGH0016",
		"Redundant Add<T> inside AddNamespace<T>",
		"AddNamespace<{0}> already registers public commands from that type; remove the inner Add<{0}> call.",
		"Argh",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor NamespaceSegmentUnresolved = new(
		"AGH0017",
		"Namespace segment could not be resolved",
		"AddNamespace<{0}>() without a name requires [NamespaceSegment] with a string argument on the type and/or a single <c>segment</c> in the type XML <summary>.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor NamespaceSegmentConflict = new(
		"AGH0018",
		"Conflicting namespace segment",
		"Namespace segment for '{0}' is specified as '{1}' in one place and '{2}' in another; use a single source.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor MultipleDefaultCommandAttributes = new(
		"AGH0019",
		"Multiple [DefaultCommand] attributes",
		"Type '{0}' has more than one method marked [DefaultCommand]; keep at most one.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor VacuousNamespace = new(
		"AGH0020",
		"Namespace registers no commands",
		"This AddNamespace block does not register any commands, nested namespaces, or default handlers.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor CommandMustInjectOptions = new(
		"AGH0021",
		"Command does not inject required options type",
		"'{0}' must inject '{1}' as a method parameter or constructor parameter.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor NamespaceSegmentSanitizationCollision = new(
		"AGH0022",
		"Namespace segment names collide after identifier sanitization",
		"Namespace segment names '{0}' and '{1}' collide after identifier sanitization (both become '{2}').",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

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

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		// ── Assembly metadata ── stable, only changes on version bump
		var assemblyInfo = context.CompilationProvider
			.Select(static (c, _) => (
				Name: c.Assembly.Identity.Name ?? "app",
				Ver: c.Assembly.Identity.Version?.ToString() ?? "0.0.0.0"));

		// ── Parse options ── changes only when LangVersion/nullable/defines change
		var parseOpts = context.ParseOptionsProvider
			.Select(static (o, _) => o as CSharpParseOptions ?? CSharpParseOptions.Default);

		// ── Capabilities from metadata references ──
		var capabilities = context.MetadataReferencesProvider
			.Collect()
			.Select(static (refs, _) => ReferenceMetadataCapabilities.Compute(refs));

		// ── Per-invocation semantic analysis — cached per-invocation by Roslyn ──
		// Each invocation is analyzed independently and produces a symbol-free AnalyzedInvocation.
		// Roslyn caches the result per invocation; only changed invocations are re-analyzed.
		var analyzed = context.SyntaxProvider
			.CreateSyntaxProvider(
				static (node, _) => node is InvocationExpressionSyntax
				{
					Expression: MemberAccessExpressionSyntax { Name: SimpleNameSyntax name }
				} && name.Identifier.Text is "Add" or "AddNamespace" or "AddRootCommand" or "AddNamespaceRootCommand"
					or "GlobalOptions" or "CommandNamespaceOptions" or "UseMiddleware",
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
			.Combine(parseOpts);

		context.RegisterSourceOutput(combined, static (spc, tuple) =>
		{
			var (((analyzedArray, (asmName, asmVer)), caps), po) = tuple;
			Execute(spc, analyzedArray, asmName, asmVer, caps, po);
		});
	}


	/// <summary>New Execute — fully incremental: no Compilation reference, works with symbol-free AnalyzedInvocation[].</summary>
	private static void Execute(
		SourceProductionContext context,
		ImmutableArray<AnalyzedInvocation> analyzed,
		string entryAsmName,
		string entryAsmVersion,
		ReferenceMetadataCapabilities.Capabilities referenceCapabilities,
		CSharpParseOptions parseOpts)
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

		EmitApp(context, appModel, parseOpts, entryAsmName, entryAsmVersion, referenceCapabilities);
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

	private sealed class RegistryNode
	{
		public CommandModel? RootCommand;
		public readonly List<CommandModel> Commands = new();
		public readonly List<NamedCommandNamespaceChild> Children = new();
		public Location? CommandNamespaceOptionsLocation;
		public OptionsTypeModel? CommandNamespaceOptionsModel;
		/// <summary>Inner XML of <c>&lt;summary&gt;</c> from the namespace entry type (populated when a generic <c>AddNamespace&lt;T&gt;</c> is used).</summary>
		public string SummaryInnerXml = "";
		/// <summary>Inner XML of <c>&lt;remarks&gt;</c> from the namespace entry type.</summary>
		public string RemarksInnerXml = "";

		public sealed class NamedCommandNamespaceChild
		{
			public string Segment = "";
			public RegistryNode Node = null!;
			/// <summary>First non-empty XML summary from the first generic <c>Add</c> handler type in this namespace block.</summary>
			public string SummaryOneLiner = "";
			public Location Location = Location.None;
		}
	}

	private sealed class AppEmitModel
	{
		public OptionsTypeModel? GlobalOptionsModel;
		public readonly RegistryNode Root = new();
		public ImmutableArray<CommandModel> AllCommands = ImmutableArray<CommandModel>.Empty;
		public ImmutableArray<GlobalMiddlewareRegistration> GlobalMiddleware = ImmutableArray<GlobalMiddlewareRegistration>.Empty;
		public readonly List<ArglessNamespaceCodegenEntry> ArglessNamespaceCodegen = new();
		/// <summary>Pre-computed injection chains per command (keyed by <see cref="CommandModel.RunMethodName"/>). Set once in <c>TryBuildAppEmitModel</c> after <c>AllCommands</c> is populated.</summary>
		public ImmutableDictionary<string, ImmutableArray<(string TypeFq, string TypeMetadataName, ImmutableArray<string> AllBaseTypeMetadataNames, string StaticFieldName, string LocalVarName, ImmutableArray<ParameterModel> FlatMembers, ImmutableArray<string>? BestCtorParamOrder)>> InjectionChains
			= ImmutableDictionary<string, ImmutableArray<(string, string, ImmutableArray<string>, string, string, ImmutableArray<ParameterModel>, ImmutableArray<string>?)>>.Empty;
	}

	private sealed record ArglessNamespaceCodegenEntry(string TypeFq, string Segment);

	private sealed record GlobalMiddlewareRegistration(string TypeFq, bool HasParameterlessCtor);

	private sealed record OptionsTypeModel(
		string TypeFq,
		string TypeMetadataName,
		ImmutableArray<string> AllBaseTypeMetadataNames,
		ImmutableArray<ParameterModel> Members,
		ImmutableArray<ParameterModel> FlattenedMembers,
		/// <summary>Parameter names of the best public non-empty constructor whose parameters all match member names; null if none or property-init should be used.</summary>
		ImmutableArray<string>? BestCtorParamOrder,
		bool IsPublic,
		bool IsGeneric);

	/// <summary>Per-parameter data extracted at analysis time, stored in <see cref="CommandModel"/>.</summary>
	private sealed record HandlerParam(
		string Name,
		string TypeMetadataName,
		bool IsInjectedParam,
		bool IsAsParameters,
		string? AsParametersPrefix,
		/// <summary>Non-null only for [AsParameters]-annotated params — the FQ type name for DTO building in emit.</summary>
		string? AsParamTypeFq = null,
		bool AsParamIsPublic = true,
		bool AsParamIsGeneric = false,
		/// <summary>Pre-computed best ctor param order for [AsParameters] DTO construction (symbol-free).</summary>
		ImmutableArray<string>? AsParamBestCtorParamOrder = null);

	private readonly record struct AsParametersMeta(
		string OwnerParamName,
		int MemberOrder,
		string TypeFq,
		bool UseInit,
		string ClrName);

	/// <summary>
	/// Value-type location snapshot used in pipeline records instead of <see cref="Location"/> (a reference type
	/// that embeds a SyntaxTree reference and breaks incremental caching on every file edit).
	/// Reconstructed to a real <see cref="Location"/> only when reporting a diagnostic.
	/// </summary>
	private readonly record struct SourceSpanInfo(
		string FilePath,
		int Start,
		int Length,
		int Line,
		int Character)
	{
		public static readonly SourceSpanInfo None = new("", 0, 0, 0, 0);

		public static SourceSpanInfo From(Location loc)
		{
			if (!loc.IsInSource) return None;
			var lp = loc.GetLineSpan();
			return new SourceSpanInfo(
				lp.Path,
				loc.SourceSpan.Start,
				loc.SourceSpan.Length,
				lp.StartLinePosition.Line,
				lp.StartLinePosition.Character);
		}

		public Location ToLocation() =>
			FilePath.Length == 0
				? Location.None
				: Location.Create(
					FilePath,
					new TextSpan(Start, Length),
					new LinePositionSpan(
						new LinePosition(Line, Character),
						new LinePosition(Line, Character + Length)));
	}

	/// <summary>
	/// Value-type diagnostic snapshot used in AnalyzedInvocation records instead of <see cref="Diagnostic"/>
	/// (a reference type that breaks incremental caching). Reconstructed in TryBuildAppEmitModel.
	/// </summary>
	private readonly record struct PendingDiagnostic(
		string DescriptorId,
		SourceSpanInfo Span,
		string Arg0 = "",
		string Arg1 = "");

	// ─── AnalyzedInvocation discriminated union ────────────────────────────────
	// Symbol-free records representing each pre-analysed ArghApp builder invocation.
	// Produced by AnalyzeInvocation() in the Select step (which has SemanticModel),
	// and consumed by TryBuildAppEmitModel() in the RegisterSourceOutput Execute step.
	// All AnalyzedInvocation subtypes are symbol-free: only strings, value types, and pre-computed
	// ImmutableArrays. No ISymbol references. This ensures Roslyn's pipeline can cache them by
	// structural equality between compilations.

	private abstract record AnalyzedInvocation(string FilePath, int SpanStart);

	/// <summary>A <c>GlobalOptions&lt;T&gt;()</c> invocation — only valid at root scope.</summary>
	private sealed record AIGlobalOptions(string FilePath, int SpanStart, OptionsTypeModel Model)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>A <c>CommandNamespaceOptions&lt;T&gt;()</c> invocation — only valid inside a namespace.</summary>
	private sealed record AICommandNamespaceOptions(string FilePath, int SpanStart, OptionsTypeModel Model)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>A <c>UseMiddleware&lt;T&gt;()</c> invocation — only valid at root scope.</summary>
	private sealed record AIUseMiddleware(string FilePath, int SpanStart, GlobalMiddlewareRegistration Registration)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>
	/// An <c>Add&lt;T&gt;()</c> or <c>Add(name, handler)</c> invocation.
	/// For <c>Add&lt;T&gt;</c>, <see cref="TypeSnapshot"/> holds the full registry structure;
	/// for <c>Add(name, handler)</c>, <see cref="Commands"/> holds the single command.
	/// </summary>
	private sealed record AIAddCommand(string FilePath, int SpanStart, ImmutableArray<CommandModel> Commands, RegistryNodeSnapshot? TypeSnapshot = null)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>An <c>AddRootCommand(handler)</c> or <c>AddNamespaceRootCommand(handler)</c> invocation.</summary>
	private sealed record AIAddRootCommand(string FilePath, int SpanStart, CommandModel Cmd, bool IsNamespaceRoot)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>
	/// An <c>AddNamespace(…)</c> invocation.
	/// LambdaBodyStart/End are character offsets into FilePath used to identify child invocations positionally.
	/// </summary>
	private sealed record AIAddNamespace(
		string FilePath,
		int SpanStart,
		string SegmentName,
		int LambdaBodyStart,
		int LambdaBodyEnd,
		/// <summary>FQ name of the generic type argument (for AddNamespace&lt;T&gt;), or null for AddNamespace(string, string, Action).</summary>
		string? EntryTypeFq,
		/// <summary>True when AddNamespace&lt;T&gt;(Action) with no explicit segment — requires codegen module initializer.</summary>
		bool IsArglessSegment,
		/// <summary>Pre-computed namespace summary one-liner for help listing.</summary>
		string NsSummary,
		/// <summary>Pre-computed namespace XML documentation.</summary>
		string NsSummaryInnerXml,
		string NsRemarksInnerXml,
		/// <summary>Whether a redundancy check should be applied (AddNamespace&lt;T&gt; registers its own commands).</summary>
		bool HasEntryType,
		SourceSpanInfo DiagnosticSpanInfo,
		/// <summary>Embedded diagnostics to report from TryBuildAppEmitModel (e.g. AGH0016 redundant Add&lt;T&gt;).</summary>
		ImmutableArray<PendingDiagnostic> EmbeddedDiagnostics,
		/// <summary>
		/// Pre-registered commands and sub-namespaces from the entry type T (for AddNamespace&lt;T&gt;).
		/// Contains root commands, regular commands, and nested children from ExpandTypeRegistration.
		/// Null when there is no entry type.
		/// </summary>
		RegistryNodeSnapshot? EntryTypeSnapshot)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>Symbol-free snapshot of a RegistryNode subtree produced during analysis.</summary>
	private sealed record RegistryNodeSnapshot(
		CommandModel? RootCommand,
		ImmutableArray<CommandModel> Commands,
		ImmutableArray<ChildNamespaceSnapshot> Children,
		string SummaryInnerXml,
		string RemarksInnerXml);

	/// <summary>Symbol-free snapshot of a child namespace (nested type) produced during analysis.</summary>
	private sealed record ChildNamespaceSnapshot(
		string Segment,
		RegistryNodeSnapshot Node,
		string SummaryOneLiner);

	// ─────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Lightweight diagnostic collection wrapper used in <see cref="AnalyzeInvocation"/> where no
	/// <see cref="SourceProductionContext"/> is available. Collected diagnostics are embedded in the
	/// returned <see cref="AnalyzedInvocation"/> record and reported later by TryBuildAppEmitModel.
	/// </summary>
	private sealed class DiagnosticAccumulator
	{
		private List<PendingDiagnostic>? _diagnostics;

		public void Add(DiagnosticDescriptor descriptor, Location location, params string[] args) =>
			(_diagnostics ??= new()).Add(new PendingDiagnostic(
				descriptor.Id,
				SourceSpanInfo.From(location),
				args.Length > 0 ? args[0] : "",
				args.Length > 1 ? args[1] : ""));

		public ImmutableArray<PendingDiagnostic> ToImmutable() =>
			_diagnostics is null ? ImmutableArray<PendingDiagnostic>.Empty : _diagnostics.ToImmutableArray();
	}

	private static DiagnosticDescriptor GetDescriptorById(string id) => id switch
	{
		"AGH0002" => HandlerMustBeMethod,
		"AGH0003" => ArgumentOrder,
		"AGH0004" => CommandNamespaceOptionsMustExtendParent,
		"AGH0005" => CommandNamespaceOptionsRequiresParent,
		"AGH0006" => UseMiddlewareDelegateNotSupported,
		"AGH0007" => DuplicateCliNames,
		"AGH0008" => CollectionPositionalNotSupported,
		"AGH0009" => AsParametersEmptyType,
		"AGH0010" => DuplicateRootCommand,
		"AGH0011" => AddRootCommandOnlyAtAppRoot,
		"AGH0012" => AddNamespaceRootCommandOnlyInNamespace,
		"AGH0013" => ReservedCommandNameRoot,
		"AGH0014" => AddNamespaceRequiresExplicitDescriptionOrType,
		"AGH0015" => AddNamespaceDescriptionNotConstant,
		"AGH0016" => RedundantAddInsideAddNamespaceT,
		"AGH0017" => NamespaceSegmentUnresolved,
		"AGH0018" => NamespaceSegmentConflict,
		"AGH0019" => MultipleDefaultCommandAttributes,
		"AGH0020" => VacuousNamespace,
		"AGH0021" => CommandMustInjectOptions,
		"AGH0022" => NamespaceSegmentSanitizationCollision,
		_ => throw new ArgumentException($"Unknown diagnostic id: {id}")
	};

	/// <summary>
	/// Per-invocation semantic analysis, intended for the CreateSyntaxProvider Select step.
	/// Runs with a <see cref="SemanticModel"/> but produces a fully symbol-free <see cref="AnalyzedInvocation"/>
	/// so the pipeline boundary data is stable across unrelated edits.
	/// Diagnostics that cannot be reported here (no SourceProductionContext in Select step) are embedded
	/// in the returned record via EmbeddedDiagnostics and reported later by TryBuildAppEmitModel.
	/// </summary>
	private static AnalyzedInvocation? AnalyzeInvocation(
		InvocationExpressionSyntax invocation,
		SemanticModel semanticModel,
		CancellationToken ct)
	{
		if (semanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
			return null;

		var filePath = invocation.SyntaxTree.FilePath;
		var spanStart = invocation.SpanStart;
		var parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;

		switch (method.Name)
		{
			case "GlobalOptions" when method.IsGenericMethod && method.TypeArguments.Length > 0:
			{
				if (method.TypeArguments[0] is not INamedTypeSymbol go || go.TypeKind == TypeKind.Error)
					return null;
				var model = BuildOptionsTypeModel(go);
				if (model is null) return null;
				return new AIGlobalOptions(filePath, spanStart, model);
			}
			case "CommandNamespaceOptions" when method.IsGenericMethod && method.TypeArguments.Length > 0:
			{
				if (method.TypeArguments[0] is not INamedTypeSymbol gt || gt.TypeKind == TypeKind.Error)
					return null;
				var model = BuildOptionsTypeModel(gt);
				if (model is null) return null;
				return new AICommandNamespaceOptions(filePath, spanStart, model);
			}
			case "UseMiddleware" when method.IsGenericMethod && method.TypeArguments.Length == 1:
			{
				if (method.TypeArguments[0] is not INamedTypeSymbol mwType || mwType.TypeKind == TypeKind.Error)
					return null;
				var reg = new GlobalMiddlewareRegistration(
					mwType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
					HasPublicParameterlessCtor(mwType));
				return new AIUseMiddleware(filePath, spanStart, reg);
			}
			case "UseMiddleware":
				// Inline delegate — diagnostic will be reported by TryBuildAppEmitModel (option 2 from plan).
				return new AIUseMiddleware(filePath, spanStart, new GlobalMiddlewareRegistration("", false));
			case "Add" when method.IsGenericMethod && method.TypeArguments.Length > 0:
			{
				if (method.TypeArguments[0] is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error)
					return null;
				// Use mergeOuterTypeSegment: false — at root this creates a child namespace wrapper.
				// At namespace level (mergeOuterTypeSegment: true in old code), this also works:
				// TryBuildAppEmitModel determines the merge behavior from context.
				var acc = new DiagnosticAccumulator();
				var wrapper = new RegistryNode();
				ExpandTypeRegistrationAcc(acc, invocation.GetLocation(), named, ImmutableArray<string>.Empty, mergeOuterTypeSegment: false, wrapper, parseOpts);
				var snap = BuildRegistryNodeSnapshot(wrapper);
				return new AIAddCommand(filePath, spanStart, ImmutableArray<CommandModel>.Empty, TypeSnapshot: snap);
			}
			case "Add" when invocation.ArgumentList.Arguments.Count >= 2:
			{
				var nameExpr = invocation.ArgumentList.Arguments[0].Expression;
				var commandName = TryGetStringLiteral(nameExpr);
				if (commandName is null || string.IsNullOrWhiteSpace(commandName))
					return null;
				var handlerExpr = invocation.ArgumentList.Arguments[1].Expression;
				if (handlerExpr is LambdaExpressionSyntax)
				{
					var node = new RegistryNode();
					TryExpandLambdaDelegateAcc(semanticModel, invocation, handlerExpr, commandName, ImmutableArray<string>.Empty, node);
					if (node.Commands.Count == 0) return null;
					return new AIAddCommand(filePath, spanStart, node.Commands.ToImmutableArray());
				}
				var handler = ResolveHandlerMethodForAnalyze(semanticModel, handlerExpr);
				if (handler is null) return null;
				var acc2 = new DiagnosticAccumulator();
				var cmd = CommandModel.FromMethod(commandName, handler, parseOpts, ImmutableArray<string>.Empty, acc2, invocation.GetLocation());
				return new AIAddCommand(filePath, spanStart, ImmutableArray.Create(cmd));
			}
			case "AddRootCommand":
			{
				if (invocation.ArgumentList.Arguments.Count < 1) return null;
				return AnalyzeAddRootCommandInvocation(invocation, semanticModel, filePath, spanStart, parseOpts, isNamespaceRoot: false);
			}
			case "AddNamespaceRootCommand":
			{
				if (invocation.ArgumentList.Arguments.Count < 1) return null;
				return AnalyzeAddRootCommandInvocation(invocation, semanticModel, filePath, spanStart, parseOpts, isNamespaceRoot: true);
			}
			case "AddNamespace":
				return AnalyzeAddNamespaceInvocation(invocation, semanticModel, filePath, spanStart, parseOpts, ct);
			default:
				return null;
		}
	}

	private static AIAddRootCommand? AnalyzeAddRootCommandInvocation(
		InvocationExpressionSyntax invocation,
		SemanticModel semanticModel,
		string filePath,
		int spanStart,
		CSharpParseOptions parseOpts,
		bool isNamespaceRoot)
	{
		if (invocation.ArgumentList.Arguments.Count < 1) return null;
		var handlerExpr = invocation.ArgumentList.Arguments[0].Expression;
		if (handlerExpr is LambdaExpressionSyntax)
		{
			var node = new RegistryNode();
			TryExpandLambdaRootCommandAcc(semanticModel, invocation, handlerExpr, ImmutableArray<string>.Empty, node);
			if (node.RootCommand is null) return null;
			return new AIAddRootCommand(filePath, spanStart, node.RootCommand, isNamespaceRoot);
		}
		var handler = ResolveHandlerMethodForAnalyze(semanticModel, handlerExpr);
		if (handler is null) return null;
		var acc = new DiagnosticAccumulator();
		var cmd = CommandModel.FromRootMethod(handler, parseOpts, ImmutableArray<string>.Empty, acc, invocation.GetLocation());
		return new AIAddRootCommand(filePath, spanStart, cmd, isNamespaceRoot);
	}

	/// <summary>Resolves a method from a handler expression without reporting diagnostics — returns null on failure.</summary>
	private static IMethodSymbol? ResolveHandlerMethodForAnalyze(SemanticModel model, ExpressionSyntax handlerExpr)
	{
		var symbol = model.GetSymbolInfo(handlerExpr).Symbol;
		if (symbol is IMethodSymbol m) return m;

		var op = model.GetOperation(handlerExpr);
		while (op is IConversionOperation conv)
			op = conv.Operand;

		if (op is IMethodReferenceOperation directRef) return directRef.Method;
		if (op is IDelegateCreationOperation del && del.Target is IMethodReferenceOperation reference) return reference.Method;

		return null; // handler not a method — diagnostic will be reported by old path / TryBuildAppEmitModel
	}

	private static AIAddNamespace? AnalyzeAddNamespaceInvocation(
		InvocationExpressionSyntax invocation,
		SemanticModel semanticModel,
		string filePath,
		int spanStart,
		CSharpParseOptions parseOpts,
		CancellationToken ct)
	{
		if (invocation.ArgumentList.Arguments.Count < 1)
			return null;

		if (semanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol addNsMethod || addNsMethod.Name != "AddNamespace")
			return null;

		var genericEntry = addNsMethod.IsGenericMethod && addNsMethod.TypeArguments.Length == 1;
		var namespaceEntryType = genericEntry && addNsMethod.TypeArguments[0] is INamedTypeSymbol nt && nt.TypeKind != TypeKind.Error
			? nt
			: null;

		var argCount = invocation.ArgumentList.Arguments.Count;
		string? segmentName = null;
		var nsSummary = "";
		var nsSummaryXml = "";
		var nsRemarksXml = "";
		var isArgless = false;

		if (genericEntry && argCount == 1 && namespaceEntryType is not null)
		{
			var firstExpr = invocation.ArgumentList.Arguments[0].Expression;
			var strOnly = TryGetStringLiteral(firstExpr) ?? TryGetStringConstant(semanticModel, firstExpr);
			if (strOnly is not null && !string.IsNullOrWhiteSpace(strOnly))
			{
				// AddNamespace<T>("segment") — no configure callback
				segmentName = strOnly;
				nsSummary = GetTypeListingSummaryOneLiner(namespaceEntryType);
			}
			else
			{
				// AddNamespace<T>(Action<IArghNamespaceBuilder>) — segment from attribute/XML
				if (!TryGetNamespaceSegmentAttribute(namespaceEntryType, out var attrSeg) &&
				    !TryGetFirstCodeInTypeSummary(namespaceEntryType, out attrSeg))
					return null; // can't determine segment — will be caught as AGH0017 in old path
				segmentName = attrSeg;
				nsSummary = GetTypeListingSummaryOneLiner(namespaceEntryType);
				isArgless = true;
			}
		}
		else if (genericEntry && argCount >= 2 && namespaceEntryType is not null)
		{
			segmentName = TryGetStringLiteral(invocation.ArgumentList.Arguments[0].Expression);
			if (string.IsNullOrWhiteSpace(segmentName))
				return null;
			nsSummary = GetTypeListingSummaryOneLiner(namespaceEntryType);
		}
		else if (!genericEntry && argCount >= 3)
		{
			segmentName = TryGetStringLiteral(invocation.ArgumentList.Arguments[0].Expression);
			if (string.IsNullOrWhiteSpace(segmentName))
				return null;
			var desc = TryGetStringConstant(semanticModel, invocation.ArgumentList.Arguments[1].Expression);
			nsSummary = desc ?? "";
		}
		else
		{
			return null; // AGH0014 emitted in old path
		}

		// Get XML docs if entry type is available.
		if (namespaceEntryType is not null)
		{
			var (sx, rx) = Documentation.GetTypeDocumentation(namespaceEntryType.GetDocumentationCommentXml());
			nsSummaryXml = sx;
			nsRemarksXml = rx;
		}

		// Determine the lambda body span for positional child lookup.
		var lambdaBodyStart = -1;
		var lambdaBodyEnd = -1;
		// The last argument is the configure lambda (if it exists)
		var lastArg = invocation.ArgumentList.Arguments.LastOrDefault();
		if (lastArg?.Expression is LambdaExpressionSyntax lambdaSyntax)
		{
			lambdaBodyStart = lambdaSyntax.Body.SpanStart;
			lambdaBodyEnd = lambdaSyntax.Body.Span.End;
		}

		// Pre-compute entry type snapshot (commands from the type, nested classes as child namespaces).
		RegistryNodeSnapshot? entryTypeSnapshot = null;
		if (namespaceEntryType is not null)
		{
			var acc = new DiagnosticAccumulator();
			var entryNode = new RegistryNode();
			// Use mergeOuterTypeSegment=true — expand the type's own methods + nested classes
			ExpandTypeRegistrationAcc(acc, invocation.GetLocation(), namespaceEntryType, ImmutableArray<string>.Empty, mergeOuterTypeSegment: true, entryNode, parseOpts);
			entryTypeSnapshot = BuildRegistryNodeSnapshot(entryNode);
		}

		return new AIAddNamespace(
			filePath,
			spanStart,
			segmentName!,
			lambdaBodyStart,
			lambdaBodyEnd,
			namespaceEntryType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
			isArgless,
			nsSummary,
			nsSummaryXml,
			nsRemarksXml,
			HasEntryType: namespaceEntryType is not null,
			SourceSpanInfo.From(invocation.GetLocation()),
			ImmutableArray<PendingDiagnostic>.Empty,
			entryTypeSnapshot);
	}

	/// <summary>Recursively expands type registration using DiagnosticAccumulator (for Select-step analysis).</summary>
	private static void ExpandTypeRegistrationAcc(
		DiagnosticAccumulator acc,
		Location location,
		INamedTypeSymbol type,
		ImmutableArray<string> routePrefix,
		bool mergeOuterTypeSegment,
		RegistryNode attachTo,
		CSharpParseOptions parseOpts)
	{
		if (mergeOuterTypeSegment)
		{
			AddMethodsFromTypeAcc(acc, location, type, routePrefix, attachTo, parseOpts);
			foreach (var nested in GetPublicNestedClasses(type))
			{
				var seg = Naming.ToTypeSegmentName(nested.Name);
				var childNode = new RegistryNode();
				var nestedPrefix = AppendSegment(routePrefix, seg);
				ExpandTypeRegistrationAcc(acc, location, nested, nestedPrefix, mergeOuterTypeSegment: true, childNode, parseOpts);
				attachTo.Children.Add(new RegistryNode.NamedCommandNamespaceChild
				{
					Segment = seg,
					Node = childNode,
					SummaryOneLiner = GetTypeListingSummaryOneLiner(nested),
					Location = location
				});
			}
		}
		else
		{
			var seg = Naming.ToTypeSegmentName(type.Name);
			var wrapper = new RegistryNode();
			var outerPrefix = AppendSegment(routePrefix, seg);
			ExpandTypeRegistrationAcc(acc, location, type, outerPrefix, mergeOuterTypeSegment: true, wrapper, parseOpts);
			attachTo.Children.Add(new RegistryNode.NamedCommandNamespaceChild
			{
				Segment = seg,
				Node = wrapper,
				SummaryOneLiner = GetTypeListingSummaryOneLiner(type),
				Location = location
			});
		}
	}

	/// <summary>Converts a RegistryNode to a symbol-free RegistryNodeSnapshot.</summary>
	private static RegistryNodeSnapshot BuildRegistryNodeSnapshot(RegistryNode node)
	{
		var children = ImmutableArray.CreateBuilder<ChildNamespaceSnapshot>(node.Children.Count);
		foreach (var ch in node.Children)
			children.Add(new ChildNamespaceSnapshot(ch.Segment, BuildRegistryNodeSnapshot(ch.Node), ch.SummaryOneLiner));
		return new RegistryNodeSnapshot(
			node.RootCommand,
			node.Commands.ToImmutableArray(),
			children.ToImmutable(),
			node.SummaryInnerXml,
			node.RemarksInnerXml);
	}

	// ─────────────────────────────────────────────────────────────────────────────


	/// <summary>
	/// Overload that builds the emit model from pre-analyzed (symbol-free) invocations — used by the truly incremental pipeline.
	/// </summary>
	private static bool TryBuildAppEmitModel(
		SourceProductionContext context,
		ImmutableArray<AnalyzedInvocation> allAnalyzed,
		out AppEmitModel? model)
	{
		model = null;

		// Report any embedded diagnostics collected during AnalyzeInvocation.
		foreach (var ai in allAnalyzed)
		{
			if (ai is AIAddNamespace ns)
				foreach (var pd in ns.EmbeddedDiagnostics)
					context.ReportDiagnostic(Diagnostic.Create(GetDescriptorById(pd.DescriptorId), pd.Span.ToLocation(), pd.Arg0, pd.Arg1));
		}

		var sorted = allAnalyzed
			.OrderBy(a => a.FilePath, StringComparer.Ordinal)
			.ThenBy(a => a.SpanStart)
			.ToList();

		// Identify root-level invocations: those NOT contained inside any AIAddNamespace lambda body.
		var rootAnalyzed = new List<AnalyzedInvocation>();
		foreach (var ai in sorted)
		{
			if (!IsInsideAnyAddNamespaceLambda(ai, sorted))
				rootAnalyzed.Add(ai);
		}

		var app = new AppEmitModel();

		// Collect global middleware from root UseMiddleware invocations.
		var mwBuilder = ImmutableArray.CreateBuilder<GlobalMiddlewareRegistration>();
		foreach (var ai in rootAnalyzed)
		{
			if (ai is AIUseMiddleware { Registration: { TypeFq: { Length: > 0 } } reg })
				mwBuilder.Add(reg);
			else if (ai is AIUseMiddleware { Registration: { TypeFq: "" } })
				context.ReportDiagnostic(Diagnostic.Create(UseMiddlewareDelegateNotSupported, ai.GetType() == typeof(AIUseMiddleware) ? Location.None : Location.None));
		}
		app.GlobalMiddleware = mwBuilder.ToImmutable();

		ProcessAnalyzedInvocationsForNode(context, sorted, rootAnalyzed, app.Root, ImmutableArray<string>.Empty, app, isRoot: true);

		ValidateCommandNamespaceOptionsChain(context, app.Root, parentEffectiveOptionsMetadataName: app.GlobalOptionsModel?.TypeMetadataName);
		if (!ValidateNamespaceSegmentSanitizationCollisions(context, app.Root))
			return false;

		// OptionsModels are already set from AIGlobalOptions / AICommandNamespaceOptions via ProcessAnalyzedInvocationsForNode.

		var flat = new List<CommandModel>();
		CollectCommands(app.Root, flat);
		model = app;
		if (flat.Count == 0)
			return false;

		var dedup = new Dictionary<string, CommandModel>(StringComparer.OrdinalIgnoreCase);
		foreach (var c in flat)
		{
			var key = string.Join("/", c.RoutePrefix) + "/" + c.CommandName;
			dedup[key] = c;
		}

		app.AllCommands = dedup.Values.ToImmutableArray();
		// GlobalOptionsModel is set during ProcessAnalyzedInvocationsForNode.

		// Pre-compute injection chains once per command; reused by validation, FixOptionsParamsInCommands, and emit.
		app.InjectionChains = app.AllCommands.ToImmutableDictionary(
			cmd => cmd.RunMethodName,
			cmd => cmd.HandlerHasNoOptionsInjection
				? ImmutableArray<(string, string, ImmutableArray<string>, string, string, ImmutableArray<ParameterModel>, ImmutableArray<string>?)>.Empty
				: BuildOptionsInjectionChain(app, cmd),
			StringComparer.Ordinal);

		ValidateCommandOptionsInjection(context, app);
		FixOptionsParamsInCommands(app);

		return true;
	}

	/// <summary>Determines if a given AnalyzedInvocation is positionally inside any AIAddNamespace lambda body.</summary>
	private static bool IsInsideAnyAddNamespaceLambda(AnalyzedInvocation ai, List<AnalyzedInvocation> all)
	{
		foreach (var other in all)
		{
			if (other is not AIAddNamespace ns) continue;
			if (ns.LambdaBodyStart < 0 || ns.LambdaBodyEnd < 0) continue;
			if (!string.Equals(ns.FilePath, ai.FilePath, StringComparison.Ordinal)) continue;
			if (ai.SpanStart > ns.LambdaBodyStart && ai.SpanStart < ns.LambdaBodyEnd)
				return true;
		}
		return false;
	}

	/// <summary>Builds the registry tree from pre-analyzed invocations for a given node scope.</summary>
	private static void ProcessAnalyzedInvocationsForNode(
		SourceProductionContext context,
		List<AnalyzedInvocation> allAnalyzed,
		List<AnalyzedInvocation> nodeInvocations,
		RegistryNode node,
		ImmutableArray<string> currentPath,
		AppEmitModel app,
		bool isRoot)
	{
		foreach (var ai in nodeInvocations)
		{
			switch (ai)
			{
				case AIGlobalOptions g when isRoot:
					app.GlobalOptionsModel = g.Model;
					break;
				case AIGlobalOptions when !isRoot:
					context.ReportDiagnostic(Diagnostic.Create(
						CommandNamespaceOptionsRequiresParent,
						Location.None,
						"T"));
					break;
				case AICommandNamespaceOptions ns when !isRoot:
					node.CommandNamespaceOptionsModel = ns.Model;
					node.CommandNamespaceOptionsLocation = Location.None;
					break;
				case AICommandNamespaceOptions when isRoot:
					context.ReportDiagnostic(Diagnostic.Create(
						CommandNamespaceOptionsRequiresParent,
						Location.None,
						"T"));
					break;
				case AIAddCommand { TypeSnapshot: { } typeSnap }:
				{
					// Add<T> invocation — apply the pre-computed snapshot.
					// At root level (isRoot=true), mergeOuterTypeSegment was false: add as child namespace.
					// Inside namespace (isRoot=false), mergeOuterTypeSegment was true: merge directly.
					if (!isRoot)
					{
						// Merge: snapshot has single child per type; extract its contents into current node.
						foreach (var childSnap in typeSnap.Children)
							ApplyRegistryNodeSnapshot(childSnap.Node, node, currentPath);
						if (typeSnap.RootCommand is { } snapRc)
							node.RootCommand ??= snapRc with { RoutePrefix = currentPath, RunMethodName = CommandModel.BuildRootDefaultRunMethodName(currentPath) };
						foreach (var snapCmd in typeSnap.Commands)
							node.Commands.Add(snapCmd with { RoutePrefix = currentPath, RunMethodName = CommandModel.BuildRunMethodNameStatic(currentPath, snapCmd.CommandName) });
					}
					else
					{
						// Not merged: add child namespaces directly.
						ApplyRegistryNodeSnapshot(typeSnap, node, currentPath);
					}
					break;
				}
				case AIAddCommand ac:
					foreach (var cmd in ac.Commands)
					{
						// Re-prefix with the current path (commands were analyzed with empty prefix).
						var prefixed = cmd with
						{
							RoutePrefix = currentPath,
							RunMethodName = currentPath.IsDefaultOrEmpty
								? cmd.RunMethodName
								: CommandModel.BuildRunMethodNameStatic(currentPath, cmd.CommandName),
							UsageHints = cmd.UsageHints
						};
						if (cmd.IsRootDefault)
							node.RootCommand = prefixed;
						else
							node.Commands.Add(prefixed);
					}
					break;
				case AIAddRootCommand rc when isRoot && rc.IsNamespaceRoot:
					context.ReportDiagnostic(Diagnostic.Create(AddNamespaceRootCommandOnlyInNamespace, Location.None));
					break;
				case AIAddRootCommand rc when !isRoot && !rc.IsNamespaceRoot:
					context.ReportDiagnostic(Diagnostic.Create(AddRootCommandOnlyAtAppRoot, Location.None));
					break;
				case AIAddRootCommand rc:
				{
					if (node.RootCommand is not null)
					{
						context.ReportDiagnostic(Diagnostic.Create(DuplicateRootCommand, Location.None));
						break;
					}
					// Re-prefix with current path.
					var prefixedRoot = rc.Cmd with
					{
						RoutePrefix = currentPath,
						RunMethodName = CommandModel.BuildRootDefaultRunMethodName(currentPath),
					};
					node.RootCommand = prefixedRoot;
					break;
				}
				case AIUseMiddleware:
					// Handled at root level for global middleware (done before this method is called).
					break;
				case AIAddNamespace ns:
					ProcessAnalyzedAddNamespace(context, allAnalyzed, ns, node, currentPath, app, isRoot);
					break;
			}
		}
	}

	private static void ProcessAnalyzedAddNamespace(
		SourceProductionContext context,
		List<AnalyzedInvocation> allAnalyzed,
		AIAddNamespace ns,
		RegistryNode parentNode,
		ImmutableArray<string> parentPath,
		AppEmitModel app,
		bool isRoot)
	{
		var childNode = new RegistryNode();
		var childPath = AppendSegment(parentPath, ns.SegmentName);

		// Find child invocations positionally.
		var childInvocations = new List<AnalyzedInvocation>();
		if (ns.LambdaBodyStart >= 0 && ns.LambdaBodyEnd >= 0)
		{
			foreach (var other in allAnalyzed)
			{
				if (!string.Equals(other.FilePath, ns.FilePath, StringComparison.Ordinal)) continue;
				if (other.SpanStart <= ns.LambdaBodyStart || other.SpanStart >= ns.LambdaBodyEnd) continue;
				// Skip invocations that are nested inside a deeper lambda (not direct children).
				if (IsInsideAnyNestedAddNamespaceLambda(other, allAnalyzed, ns)) continue;
				childInvocations.Add(other);
			}
			childInvocations.Sort((a, b) =>
			{
				var c = string.CompareOrdinal(a.FilePath, b.FilePath);
				return c != 0 ? c : a.SpanStart.CompareTo(b.SpanStart);
			});
		}

		// If we have a namespace entry type (AddNamespace<T>), apply its pre-computed snapshot.
		if (ns.EntryTypeSnapshot is { } snap)
		{
			ApplyRegistryNodeSnapshot(snap, childNode, childPath);
			childNode.SummaryInnerXml = ns.NsSummaryInnerXml;
			childNode.RemarksInnerXml = ns.NsRemarksInnerXml;
		}

		// Register argless segment codegen.
		if (ns.IsArglessSegment && ns.EntryTypeFq is { Length: > 0 } arglessFq)
		{
			foreach (var existing in app.ArglessNamespaceCodegen)
			{
				if (string.Equals(existing.TypeFq, arglessFq, StringComparison.Ordinal))
					goto skipArglessAdd;
			}
			app.ArglessNamespaceCodegen.Add(new ArglessNamespaceCodegenEntry(arglessFq, ns.SegmentName));
			skipArglessAdd:;
		}

		ProcessAnalyzedInvocationsForNode(context, allAnalyzed, childInvocations, childNode, childPath, app, isRoot: false);

		if (IsRegistryNodeVacuous(childNode))
			context.ReportDiagnostic(Diagnostic.Create(VacuousNamespace, ns.DiagnosticSpanInfo.ToLocation()));

		parentNode.Children.Add(new RegistryNode.NamedCommandNamespaceChild
		{
			Segment = ns.SegmentName,
			Node = childNode,
			SummaryOneLiner = ns.NsSummary,
			Location = ns.DiagnosticSpanInfo.ToLocation()
		});
	}

	/// <summary>Checks if an invocation is inside a nested AddNamespace lambda that is itself inside ns.</summary>
	private static bool IsInsideAnyNestedAddNamespaceLambda(AnalyzedInvocation ai, List<AnalyzedInvocation> all, AIAddNamespace parent)
	{
		foreach (var other in all)
		{
			if (other is not AIAddNamespace nested) continue;
			if (ReferenceEquals(nested, parent)) continue;
			if (nested.LambdaBodyStart < 0 || nested.LambdaBodyEnd < 0) continue;
			if (!string.Equals(nested.FilePath, ai.FilePath, StringComparison.Ordinal)) continue;
			// nested must itself be inside parent
			if (nested.SpanStart <= parent.LambdaBodyStart || nested.SpanStart >= parent.LambdaBodyEnd) continue;
			// ai must be inside nested
			if (ai.SpanStart > nested.LambdaBodyStart && ai.SpanStart < nested.LambdaBodyEnd)
				return true;
		}
		return false;
	}

	/// <summary>Applies a pre-computed RegistryNodeSnapshot to a live RegistryNode (re-prefixing commands).</summary>
	private static void ApplyRegistryNodeSnapshot(RegistryNodeSnapshot snap, RegistryNode target, ImmutableArray<string> path)
	{
		if (snap.RootCommand is { } rc)
		{
			var prefixed = rc with
			{
				RoutePrefix = path,
				RunMethodName = CommandModel.BuildRootDefaultRunMethodName(path)
			};
			// Only set if not already set by an explicit AddNamespaceRootCommand in the lambda body.
			target.RootCommand ??= prefixed;
		}
		foreach (var cmd in snap.Commands)
		{
			var prefixed = cmd with
			{
				RoutePrefix = path,
				RunMethodName = CommandModel.BuildRunMethodNameStatic(path, cmd.CommandName)
			};
			target.Commands.Add(prefixed);
		}
		foreach (var childSnap in snap.Children)
		{
			var childPath = AppendSegment(path, childSnap.Segment);
			var childNode = new RegistryNode();
			childNode.SummaryInnerXml = childSnap.Node.SummaryInnerXml;
			childNode.RemarksInnerXml = childSnap.Node.RemarksInnerXml;
			ApplyRegistryNodeSnapshot(childSnap.Node, childNode, childPath);
			target.Children.Add(new RegistryNode.NamedCommandNamespaceChild
			{
				Segment = childSnap.Segment,
				Node = childNode,
				SummaryOneLiner = childSnap.SummaryOneLiner,
				Location = Location.None
			});
		}
		target.SummaryInnerXml = snap.SummaryInnerXml;
		target.RemarksInnerXml = snap.RemarksInnerXml;
	}

	private static void ValidateCommandNamespaceOptionsChain(
		SourceProductionContext context,
		RegistryNode node,
		string? parentEffectiveOptionsMetadataName)
	{
		var nsModel = node.CommandNamespaceOptionsModel;
		if (nsModel is not null)
		{
			if (parentEffectiveOptionsMetadataName is null)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					CommandNamespaceOptionsRequiresParent,
					node.CommandNamespaceOptionsLocation ?? Location.None,
					GetShortTypeName(nsModel.TypeMetadataName)));
			}
			else if (nsModel.TypeMetadataName != parentEffectiveOptionsMetadataName
			         && !nsModel.AllBaseTypeMetadataNames.Contains(parentEffectiveOptionsMetadataName))
			{
				context.ReportDiagnostic(Diagnostic.Create(
					CommandNamespaceOptionsMustExtendParent,
					node.CommandNamespaceOptionsLocation ?? Location.None,
					GetShortTypeName(nsModel.TypeMetadataName),
					GetShortTypeName(parentEffectiveOptionsMetadataName)));
			}
		}

		var nextParent = nsModel?.TypeMetadataName ?? parentEffectiveOptionsMetadataName;
		foreach (var child in node.Children)
			ValidateCommandNamespaceOptionsChain(context, child.Node, nextParent);
	}

	private static string GetShortTypeName(string metadataName)
	{
		var dot = metadataName.LastIndexOf('.');
		return dot >= 0 ? metadataName.Substring(dot + 1) : metadataName;
	}

	private static bool ValidateNamespaceSegmentSanitizationCollisions(SourceProductionContext context, RegistryNode node)
	{
		var seen = new Dictionary<string, string>(StringComparer.Ordinal);
		var ok = true;
		foreach (var child in node.Children)
		{
			var sanitized = Naming.SanitizeIdentifier(child.Segment);
			if (seen.TryGetValue(sanitized, out var first))
			{
				context.ReportDiagnostic(Diagnostic.Create(
					NamespaceSegmentSanitizationCollision,
					child.Location,
					first,
					child.Segment,
					sanitized));
				ok = false;
			}
			else
			{
				seen[sanitized] = child.Segment;
			}
		}
		foreach (var child in node.Children)
		{
			if (!ValidateNamespaceSegmentSanitizationCollisions(context, child.Node))
				ok = false;
		}
		return ok;
	}

	/// <summary>
	/// AGH0021: every non-lambda command must inject its most specific applicable options type
	/// (global or namespace-scoped) as a method parameter or constructor parameter.
	/// </summary>
	private static void ValidateCommandOptionsInjection(SourceProductionContext context, AppEmitModel app)
	{
		foreach (var cmd in app.AllCommands)
		{
			if (cmd.IsLambda || cmd.HandlerParamTypes.IsDefaultOrEmpty && !cmd.RequiresInstance)
				continue;
			if (cmd.HandlerHasNoOptionsInjection)
				continue;

			// Most specific required options type = last entry in the injection chain.
			var chain = app.InjectionChains.TryGetValue(cmd.RunMethodName, out var precomputed)
				? precomputed
				: BuildOptionsInjectionChain(app, cmd);
			if (chain.IsEmpty)
				continue;
			var (requiredTypeFq, requiredMetaName, requiredBaseNames, _, _, _, _) = chain[chain.Length - 1];

			// Check method parameters first.
			var injected = false;
			foreach (var mp in cmd.HandlerParamTypes)
			{
				if (mp.TypeMetadataName == requiredMetaName ||
				    requiredBaseNames.Contains(mp.TypeMetadataName))
				{
					injected = true;
					break;
				}
			}

			// For instance methods, also accept injection via constructor.
			if (!injected && cmd.RequiresInstance)
			{
				foreach (var cp in cmd.ContainingTypeCtorParams)
				{
					if (cp.TypeMetadataName == requiredMetaName ||
					    requiredBaseNames.Contains(cp.TypeMetadataName))
					{
						injected = true;
						break;
					}
				}
			}

			if (!injected)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					CommandMustInjectOptions,
					cmd.HandlerSpanInfo.ToLocation(),
					cmd.MethodName,
					requiredMetaName));  // use pre-computed metadata name instead of ToDisplayString
			}
		}
	}

	/// <summary>
	/// Returns the ordered chain of options entries (global → most-specific namespace) for injection into a command.
	/// Walks the registry tree directly so namespace options types with zero own members are still included.
	/// Each entry carries the static field name (pre-parsed fallback) and a local var name (command-runner reconstruction).
	/// All fields are symbol-free (strings / pre-computed ParameterModel arrays).
	/// </summary>
	private static ImmutableArray<(string TypeFq, string TypeMetadataName, ImmutableArray<string> AllBaseTypeMetadataNames, string StaticFieldName, string LocalVarName, ImmutableArray<ParameterModel> FlatMembers, ImmutableArray<string>? BestCtorParamOrder)>
		BuildOptionsInjectionChain(AppEmitModel app, CommandModel cmd)
	{
		var result = ImmutableArray.CreateBuilder<(string, string, ImmutableArray<string>, string, string, ImmutableArray<ParameterModel>, ImmutableArray<string>?)>();
		if (app.GlobalOptionsModel is { } gom)
			result.Add((
				gom.TypeFq,
				gom.TypeMetadataName,
				gom.AllBaseTypeMetadataNames,
				OptionsStaticFieldNameFq(gom.TypeFq),
				OptionsLocalVarNameFq(gom.TypeFq),
				gom.FlattenedMembers,
				gom.BestCtorParamOrder));

		var current = app.Root;
		foreach (var seg in cmd.RoutePrefix)
		{
			RegistryNode.NamedCommandNamespaceChild? found = null;
			foreach (var ch in current.Children)
			{
				if (string.Equals(ch.Segment, seg, StringComparison.OrdinalIgnoreCase))
				{
					found = ch;
					break;
				}
			}
			if (found is null) break;
			current = found.Node;
			if (current.CommandNamespaceOptionsModel is { } nsModel)
				result.Add((
					nsModel.TypeFq,
					nsModel.TypeMetadataName,
					nsModel.AllBaseTypeMetadataNames,
					OptionsStaticFieldNameFq(nsModel.TypeFq),
					OptionsLocalVarNameFq(nsModel.TypeFq),
					nsModel.FlattenedMembers,
					nsModel.BestCtorParamOrder));
		}

		return result.ToImmutable();
	}

	/// <summary>
	/// Removes options-type parameters from each command's <see cref="CommandModel.Parameters"/> so the
	/// flag-parsing codegen ignores them. They are injected separately via static fields in <see cref="EmitInvocation"/>.
	/// </summary>
	private static void FixOptionsParamsInCommands(AppEmitModel app)
	{
		var updated = ImmutableArray.CreateBuilder<CommandModel>(app.AllCommands.Length);
		foreach (var cmd in app.AllCommands)
		{
			if (cmd.IsLambda || cmd.HandlerHasNoOptionsInjection)
			{
				updated.Add(cmd);
				continue;
			}

			var injChain = app.InjectionChains.TryGetValue(cmd.RunMethodName, out var precomputed2)
				? precomputed2
				: BuildOptionsInjectionChain(app, cmd);
			if (injChain.IsEmpty)
			{
				updated.Add(cmd);
				continue;
			}

			// Remove original options-type params; replace with OptionsInjected entries for each flattened
			// member so bool-switch / short-opt / canon-name machinery still recognises those flags.
			var filtered = cmd.Parameters.Where(p =>
			{
				if (p.AsParametersOwnerParamName is not null) return true;
				var handlerParam = cmd.HandlerParamTypes.FirstOrDefault(mp => mp.Name == p.SymbolName);
				if (handlerParam is null) return true;
				return !injChain.Any(o =>
					o.TypeMetadataName == handlerParam.TypeMetadataName ||
					o.AllBaseTypeMetadataNames.Contains(handlerParam.TypeMetadataName));
			}).ToList();

			// Add flattened options members as OptionsInjected so the flag parser handles them correctly.
			// Pre-seed with CLI names already present (e.g. from [AsParameters] expansion) to avoid duplicates.
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var p in filtered)
				if (p.Kind == ParameterKind.Flag) seen.Add(p.CliLongName);

			foreach (var (_, _, _, _, _, flatMembers, _) in injChain)
			{
				foreach (var m in flatMembers)
				{
					if (m.Kind != ParameterKind.Flag) continue;
					if (!seen.Add(m.CliLongName)) continue; // dedup inherited members
					// Create an OptionsInjected entry — only flag-recognition fields matter here.
					filtered.Add(m with { Kind = ParameterKind.OptionsInjected });
				}
			}

			var newParams = filtered.ToImmutableArray();
			updated.Add(cmd with
			{
				Parameters = newParams,
				// Rebuild usage hints now that options params are stripped.
				UsageHints = UsageSynopsis.Build(newParams)
			});
		}

		app.AllCommands = updated.ToImmutable();

		// Also update RootCommand references in RegistryNodes so help printers see the fixed parameters.
		var fixedById = new Dictionary<string, CommandModel>(StringComparer.Ordinal);
		foreach (var cmd in app.AllCommands)
			fixedById[cmd.RunMethodName] = cmd;
		UpdateRegistryNodeRootCommands(app.Root, fixedById);
	}

	private static void UpdateRegistryNodeRootCommands(RegistryNode node, Dictionary<string, CommandModel> fixedById)
	{
		if (node.RootCommand is not null && fixedById.TryGetValue(node.RootCommand.RunMethodName, out var fixedRoot))
			node.RootCommand = fixedRoot;
		foreach (var child in node.Children)
			UpdateRegistryNodeRootCommands(child.Node, fixedById);
	}

	private static bool TypeInheritsFromOrImplements(INamedTypeSymbol type, INamedTypeSymbol baseOrInterface)
	{
		var current = type;
		while (current is not null)
		{
			if (SymbolEqualityComparer.Default.Equals(current, baseOrInterface))
				return true;
			current = current.BaseType;
		}

		foreach (var iface in type.AllInterfaces)
		{
			if (SymbolEqualityComparer.Default.Equals(iface, baseOrInterface))
				return true;
		}

		return false;
	}

	private static void CollectCommands(RegistryNode node, List<CommandModel> sink)
	{
		if (node.RootCommand is { } rc)
			sink.Add(rc);
		sink.AddRange(node.Commands);
		foreach (var child in node.Children)
			CollectCommands(child.Node, sink);
	}



	private static bool TryGetNamespaceSegmentAttribute(INamedTypeSymbol type, out string segment)
	{
		segment = "";
		foreach (var ad in type.GetAttributes())
		{
			if (ad.AttributeClass?.Name != "NamespaceSegmentAttribute" ||
			    ad.AttributeClass.ContainingNamespace?.ToDisplayString() != "Nullean.Argh")
				continue;
			if (ad.ConstructorArguments.Length > 0 && ad.ConstructorArguments[0].Value is string s && !string.IsNullOrWhiteSpace(s))
			{
				segment = s;
				return true;
			}
		}

		return false;
	}

	private static bool TryGetFirstCodeInTypeSummary(INamedTypeSymbol type, out string code)
	{
		code = "";
		var xml = type.GetDocumentationCommentXml();
		if (string.IsNullOrWhiteSpace(xml))
			return false;
		try
		{
			var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
			var root = doc.Root;
			var sum = root?.Descendants().FirstOrDefault(e => e.Name.LocalName == "summary");
			var c = sum?.Descendants().FirstOrDefault(e => e.Name.LocalName == "c");
			if (c is null || string.IsNullOrWhiteSpace(c.Value))
				return false;
			code = c.Value.Trim();
			return IdentifierSegmentPattern.IsMatch(code);
		}
		catch
		{
			return false;
		}
	}

	private static bool TryResolveNamespaceSegmentForArgless(
		SourceProductionContext context,
		INamedTypeSymbol type,
		Location errorLocation,
		out string segment)
	{
		segment = "";
		var hasAttr = TryGetNamespaceSegmentAttribute(type, out var attrSeg);
		var hasXml = TryGetFirstCodeInTypeSummary(type, out var xmlSeg);
		if (!hasAttr && !hasXml)
		{
			context.ReportDiagnostic(Diagnostic.Create(NamespaceSegmentUnresolved, errorLocation, type.Name));
			return false;
		}

		if (hasAttr && hasXml && !string.Equals(attrSeg, xmlSeg, StringComparison.Ordinal))
		{
			context.ReportDiagnostic(Diagnostic.Create(NamespaceSegmentConflict, errorLocation, type.Name, attrSeg, xmlSeg));
			return false;
		}

		segment = hasAttr ? attrSeg : xmlSeg;
		return true;
	}

	private static void ValidateNamespaceSegmentForExplicitName(
		SourceProductionContext context,
		INamedTypeSymbol type,
		string literalSegment,
		Location location)
	{
		var hasAttr = TryGetNamespaceSegmentAttribute(type, out var attrSeg);
		var hasXml = TryGetFirstCodeInTypeSummary(type, out var xmlSeg);
		if (hasAttr && hasXml && !string.Equals(attrSeg, xmlSeg, StringComparison.Ordinal))
			context.ReportDiagnostic(Diagnostic.Create(NamespaceSegmentConflict, location, type.Name, attrSeg, xmlSeg));
		if (hasAttr && !string.Equals(attrSeg, literalSegment, StringComparison.Ordinal))
			context.ReportDiagnostic(Diagnostic.Create(NamespaceSegmentConflict, location, type.Name, attrSeg, literalSegment));
		if (hasXml && !string.Equals(xmlSeg, literalSegment, StringComparison.Ordinal))
			context.ReportDiagnostic(Diagnostic.Create(NamespaceSegmentConflict, location, type.Name, xmlSeg, literalSegment));
	}

	private static void RegisterArglessNamespaceCodegen(
		SourceProductionContext context,
		AppEmitModel app,
		INamedTypeSymbol type,
		string segment,
		Location location)
	{
		var typeFq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		foreach (var existing in app.ArglessNamespaceCodegen)
		{
			if (!string.Equals(existing.TypeFq, typeFq, StringComparison.Ordinal))
				continue;
			if (!string.Equals(existing.Segment, segment, StringComparison.Ordinal))
				context.ReportDiagnostic(Diagnostic.Create(NamespaceSegmentConflict, location, type.Name, existing.Segment, segment));
			return;
		}

		app.ArglessNamespaceCodegen.Add(new ArglessNamespaceCodegenEntry(typeFq, segment));
	}

	private static bool IsRegistryNodeVacuous(RegistryNode node) =>
		node.RootCommand is null && node.Commands.Count == 0 && node.Children.Count == 0;


	private static InvocationExpressionSyntax? FindParentAddNamespaceInvocation(InvocationExpressionSyntax invocation)
	{
		for (var n = invocation.Parent; n != null; n = n.Parent)
		{
			if (n is LambdaExpressionSyntax lambda && IsAddNamespaceConfigureLambda(lambda, out var addNamespaceInv))
				return addNamespaceInv;
		}

		return null;
	}

	private static bool IsAddNamespaceConfigureLambda(LambdaExpressionSyntax lambda, out InvocationExpressionSyntax addNamespaceInv)
	{
		addNamespaceInv = null!;
		if (lambda.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax al })
			return false;
		if (al.Parent is not InvocationExpressionSyntax inv)
			return false;
		if (inv.Expression is not MemberAccessExpressionSyntax ma || ma.Name is not SimpleNameSyntax sns ||
		    sns.Identifier.Text != "AddNamespace")
			return false;
		var last = al.Arguments.Count - 1;
		if (last < 0 || !ReferenceEquals(al.Arguments[last].Expression, lambda))
			return false;
		addNamespaceInv = inv;
		return true;
	}
	private static bool IsRedundantGenericAddForNamespaceEntry(
		InvocationExpressionSyntax inv,
		INamedTypeSymbol namespaceEntryType,
		Compilation compilation)
	{
		var model = compilation.GetSemanticModel(inv.SyntaxTree);
		if (model.GetSymbolInfo(inv).Symbol is not IMethodSymbol method || method.Name != "Add" || !method.IsGenericMethod)
			return false;
		if (method.TypeArguments.Length != 1)
			return false;
		if (method.TypeArguments[0] is not INamedTypeSymbol addType || addType.TypeKind == TypeKind.Error)
			return false;
		return SymbolEqualityComparer.Default.Equals(addType, namespaceEntryType);
	}

	private static void ExpandAddStringDelegate(
		SourceProductionContext context,
		SemanticModel model,
		InvocationExpressionSyntax invocation,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode)
	{
		var nameExpr = invocation.ArgumentList.Arguments[0].Expression;
		var handlerExpr = invocation.ArgumentList.Arguments[1].Expression;

		var commandName = TryGetStringLiteral(nameExpr);
		if (commandName is null || string.IsNullOrWhiteSpace(commandName))
			return;

		if (commandName.Equals("__argh_root", StringComparison.OrdinalIgnoreCase))
		{
			context.ReportDiagnostic(Diagnostic.Create(ReservedCommandNameRoot, nameExpr.GetLocation(), commandName));
			return;
		}

		// Detect lambda expressions — handle them as stored-delegate commands
		if (handlerExpr is LambdaExpressionSyntax)
		{
			TryExpandLambdaDelegate(context, model, invocation, handlerExpr, commandName, routePrefix, targetNode);
			return;
		}

		var handler = ResolveHandlerMethod(model, handlerExpr, context, invocation);
		if (handler is null)
			return;

		var parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;
		targetNode.Commands.Add(CommandModel.FromMethod(commandName, handler, parseOpts, routePrefix, context, invocation.GetLocation()));
	}

	/// <summary>Select-step (no SourceProductionContext) variant of <see cref="TryExpandLambdaDelegate"/>.</summary>
	private static void TryExpandLambdaDelegateAcc(
		SemanticModel model,
		InvocationExpressionSyntax invocation,
		ExpressionSyntax handlerExpr,
		string commandName,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode) =>
		TryExpandLambdaDelegate(default, model, invocation, handlerExpr, commandName, routePrefix, targetNode);

	private static void TryExpandLambdaDelegate(
		SourceProductionContext context,
		SemanticModel model,
		InvocationExpressionSyntax invocation,
		ExpressionSyntax handlerExpr,
		string commandName,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode)
	{
		// Get the converted delegate type via type info (the lambda is implicitly converted to Delegate)
		var op = model.GetOperation(handlerExpr);
		// Unwrap conversions
		while (op is IConversionOperation conv)
			op = conv.Operand;

		IMethodSymbol? invokeMethod = null;
		INamedTypeSymbol? delegateType = null;

		if (op is IAnonymousFunctionOperation anonFunc)
		{
			invokeMethod = anonFunc.Symbol;
			// Get the converted-to delegate type from the parent conversion
			var parent = model.GetOperation(handlerExpr);
			if (parent is IConversionOperation parentConv && parentConv.Type is INamedTypeSymbol dt)
				delegateType = dt;
		}

		if (invokeMethod is null)
			return;

		// Build the storage key: "namespace/name" for nested, "name" for root
		var storageKey = routePrefix.IsDefaultOrEmpty
			? commandName
			: string.Join("/", routePrefix) + "/" + commandName;

		// Get the FQ delegate type string for casting at runtime
		var delegateFq = delegateType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Delegate";

		var parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;

		// Build parameter models from the lambda's method symbol
		var paramBuilder = ImmutableArray.CreateBuilder<ParameterModel>();
		foreach (var p in invokeMethod.Parameters)
		{
			paramBuilder.Add(ParameterModel.From(p));
		}
		var parameters = paramBuilder.ToImmutable();
		var usage = UsageSynopsis.Build(parameters);
		// Build run method name inline (mirrors CommandModel.BuildRunMethodName)
		string runName;
		if (routePrefix.IsDefaultOrEmpty)
			runName = "Run_" + Naming.SanitizeIdentifier(commandName);
		else
		{
			var rnSb = new StringBuilder();
			rnSb.Append("Run");
			foreach (var seg in routePrefix) { rnSb.Append('_'); rnSb.Append(Naming.SanitizeIdentifier(seg)); }
			rnSb.Append('_'); rnSb.Append(Naming.SanitizeIdentifier(commandName));
			runName = rnSb.ToString();
		}
		var retFq = invokeMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var retIsVoid = retFq is "global::System.Void"
			or "global::System.Threading.Tasks.Task"
			or "global::System.Threading.Tasks.ValueTask";
		var retIsAsync = retFq is "global::System.Threading.Tasks.Task"
			or "global::System.Threading.Tasks.ValueTask"
			|| (invokeMethod.ReturnType is INamedTypeSymbol rNamed && rNamed.IsGenericType &&
			    (rNamed.ConstructedFrom.Name is "Task" or "ValueTask") &&
			    rNamed.ConstructedFrom.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks");

		var cmd = new CommandModel(
			routePrefix,
			commandName,
			runName,
			"object",
			"__lambda",
			false,
			false,
			retFq,
			retIsAsync,
			retIsVoid,
			parameters,
			false,
			ImmutableArray<HandlerParam>.Empty,
			SourceSpanInfo.None,
			ImmutableArray<(string, string)>.Empty,
			"",   // HandlerDocCommentId
			"",
			"",
			"",
			"",
			"",
			usage,
			ImmutableArray<(string, bool)>.Empty,
			IsLambda: true,
			LambdaStorageKey: storageKey,
			LambdaDelegateFq: delegateFq);

		targetNode.Commands.Add(cmd);
	}

	private const string RootDefaultInternalCommandName = "__argh_root";

	private static void ExpandAddRootCommand(
		SourceProductionContext context,
		SemanticModel model,
		InvocationExpressionSyntax invocation,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode)
	{
		if (targetNode.RootCommand is not null)
		{
			context.ReportDiagnostic(Diagnostic.Create(DuplicateRootCommand, invocation.GetLocation()));
			return;
		}

		if (invocation.ArgumentList.Arguments.Count < 1)
			return;

		var handlerExpr = invocation.ArgumentList.Arguments[0].Expression;
		if (handlerExpr is LambdaExpressionSyntax)
		{
			TryExpandLambdaRootCommand(context, model, invocation, handlerExpr, routePrefix, targetNode);
			return;
		}

		var handler = ResolveHandlerMethod(model, handlerExpr, context, invocation);
		if (handler is null)
			return;

		var parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;
		targetNode.RootCommand = CommandModel.FromRootMethod(handler, parseOpts, routePrefix, context, invocation.GetLocation());
	}

	/// <summary>Select-step (no SourceProductionContext) variant of <see cref="TryExpandLambdaRootCommand"/>.</summary>
	private static void TryExpandLambdaRootCommandAcc(
		SemanticModel model,
		InvocationExpressionSyntax invocation,
		ExpressionSyntax handlerExpr,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode) =>
		TryExpandLambdaRootCommand(default, model, invocation, handlerExpr, routePrefix, targetNode);

	private static void TryExpandLambdaRootCommand(
		SourceProductionContext context,
		SemanticModel model,
		InvocationExpressionSyntax invocation,
		ExpressionSyntax handlerExpr,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode)
	{
		var op = model.GetOperation(handlerExpr);
		while (op is IConversionOperation conv)
			op = conv.Operand;

		if (op is not IAnonymousFunctionOperation anonFunc)
			return;

		var invokeMethod = anonFunc.Symbol;
		var parent = model.GetOperation(handlerExpr);
		INamedTypeSymbol? delegateType = null;
		if (parent is IConversionOperation parentConv && parentConv.Type is INamedTypeSymbol dt)
			delegateType = dt;

		if (invokeMethod is null)
			return;

		var storageKey = routePrefix.IsDefaultOrEmpty
			? "__argh_root"
			: string.Join("/", routePrefix) + "/__argh_root";
		var delegateFq = delegateType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Delegate";
		var parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;
		var paramBuilder = ImmutableArray.CreateBuilder<ParameterModel>();
		foreach (var p in invokeMethod.Parameters)
			paramBuilder.Add(ParameterModel.From(p));
		var parameters = paramBuilder.ToImmutable();
		var usage = UsageSynopsis.Build(parameters);
		var runName = CommandModel.BuildRootDefaultRunMethodName(routePrefix);
		var lambdaRetFq = invokeMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var lambdaRetIsVoid = lambdaRetFq is "global::System.Void"
			or "global::System.Threading.Tasks.Task"
			or "global::System.Threading.Tasks.ValueTask";
		var lambdaRetIsAsync = lambdaRetFq is "global::System.Threading.Tasks.Task"
			or "global::System.Threading.Tasks.ValueTask"
			|| (invokeMethod.ReturnType is INamedTypeSymbol lrNamed && lrNamed.IsGenericType &&
			    (lrNamed.ConstructedFrom.Name is "Task" or "ValueTask") &&
			    lrNamed.ConstructedFrom.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks");
		var cmd = new CommandModel(
			routePrefix,
			RootDefaultInternalCommandName,
			runName,
			"object",
			"__lambda",
			false,
			false,
			lambdaRetFq,
			lambdaRetIsAsync,
			lambdaRetIsVoid,
			parameters,
			false,
			ImmutableArray<HandlerParam>.Empty,
			SourceSpanInfo.None,
			ImmutableArray<(string, string)>.Empty,
			"",   // HandlerDocCommentId
			"",
			"",
			"",
			"",
			"",
			usage,
			ImmutableArray<(string, bool)>.Empty,
			IsRootDefault: true,
			IsLambda: true,
			LambdaStorageKey: storageKey,
			LambdaDelegateFq: delegateFq);
		targetNode.RootCommand = cmd;
	}

	private static void ExpandTypeRegistration(
		SourceProductionContext context,
		InvocationExpressionSyntax invocation,
		INamedTypeSymbol type,
		ImmutableArray<string> routePrefix,
		bool mergeOuterTypeSegment,
		RegistryNode attachTo,
		CSharpParseOptions parseOpts)
	{
		if (mergeOuterTypeSegment)
		{
			AddMethodsFromType(context, invocation, type, routePrefix, attachTo, parseOpts);
			foreach (var nested in GetPublicNestedClasses(type))
			{
				var seg = Naming.ToTypeSegmentName(nested.Name);
				var childNode = new RegistryNode();
				var nestedPrefix = AppendSegment(routePrefix, seg);
				ExpandTypeRegistration(context, invocation, nested, nestedPrefix, mergeOuterTypeSegment: true, childNode, parseOpts);
				attachTo.Children.Add(new RegistryNode.NamedCommandNamespaceChild
				{
					Segment = seg,
					Node = childNode,
					SummaryOneLiner = GetTypeListingSummaryOneLiner(nested),
					Location = invocation.GetLocation()
				});
			}
		}
		else
		{
			var seg = Naming.ToTypeSegmentName(type.Name);
			var wrapper = new RegistryNode();
			var outerPrefix = AppendSegment(routePrefix, seg);
			ExpandTypeRegistration(context, invocation, type, outerPrefix, mergeOuterTypeSegment: true, wrapper, parseOpts);
			attachTo.Children.Add(new RegistryNode.NamedCommandNamespaceChild
			{
				Segment = seg,
				Node = wrapper,
				SummaryOneLiner = GetTypeListingSummaryOneLiner(type),
				Location = invocation.GetLocation()
			});
		}
	}

	private static ImmutableArray<string> AppendSegment(ImmutableArray<string> prefix, string segment)
	{
		var b = ImmutableArray.CreateBuilder<string>(prefix.Length + 1);
		foreach (var s in prefix)
			b.Add(s);
		b.Add(segment);
		return b.MoveToImmutable();
	}

	private static IEnumerable<INamedTypeSymbol> GetPublicNestedClasses(INamedTypeSymbol type)
	{
		foreach (var member in type.GetMembers())
		{
			if (member is INamedTypeSymbol nested && nested.TypeKind == TypeKind.Class && nested.DeclaredAccessibility == Accessibility.Public
			    && !nested.IsStatic)
				yield return nested;
		}
	}

	private static void AddMethodsFromType(
		SourceProductionContext context,
		InvocationExpressionSyntax invocation,
		INamedTypeSymbol type,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode,
		CSharpParseOptions parseOpts)
	{
		IMethodSymbol? defaultCommand = null;
		foreach (var member in type.GetMembers())
		{
			if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
				continue;
			if (method.AssociatedSymbol is not null)
				continue;
			if (method.DeclaredAccessibility != Accessibility.Public)
				continue;
			if (!HasDefaultCommandAttribute(method))
				continue;
			if (defaultCommand is not null)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					MultipleDefaultCommandAttributes,
					method.Locations.FirstOrDefault() ?? invocation.GetLocation(),
					type.Name));
				continue;
			}

			defaultCommand = method;
		}

		if (defaultCommand is not null)
		{
			if (targetNode.RootCommand is not null)
				context.ReportDiagnostic(Diagnostic.Create(DuplicateRootCommand, invocation.GetLocation()));
			else
				targetNode.RootCommand = CommandModel.FromRootMethod(defaultCommand, parseOpts, routePrefix, context, invocation.GetLocation());
		}

		foreach (var member in type.GetMembers())
		{
			if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
				continue;

			if (method.AssociatedSymbol is not null)
				continue;

			if (method.DeclaredAccessibility != Accessibility.Public)
				continue;

			if (defaultCommand is not null && SymbolEqualityComparer.Default.Equals(method, defaultCommand))
				continue;

			var cmdName = Naming.ToCommandName(method.Name);
			targetNode.Commands.Add(CommandModel.FromMethod(cmdName, method, parseOpts, routePrefix, context, invocation.GetLocation()));
		}
	}

	/// <summary>DiagnosticAccumulator-based variant of <see cref="AddMethodsFromType"/> for use in the Select-step analysis.</summary>
	private static void AddMethodsFromTypeAcc(
		DiagnosticAccumulator acc,
		Location location,
		INamedTypeSymbol type,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode,
		CSharpParseOptions parseOpts)
	{
		IMethodSymbol? defaultCommand = null;
		foreach (var member in type.GetMembers())
		{
			if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary) continue;
			if (method.AssociatedSymbol is not null) continue;
			if (method.DeclaredAccessibility != Accessibility.Public) continue;
			if (!HasDefaultCommandAttribute(method)) continue;
			if (defaultCommand is not null)
			{
				acc.Add(MultipleDefaultCommandAttributes, method.Locations.FirstOrDefault() ?? location, type.Name);
				continue;
			}
			defaultCommand = method;
		}
		if (defaultCommand is not null)
		{
			if (targetNode.RootCommand is not null)
				acc.Add(DuplicateRootCommand, location);
			else
				targetNode.RootCommand = CommandModel.FromRootMethod(defaultCommand, parseOpts, routePrefix, acc, location);
		}
		foreach (var member in type.GetMembers())
		{
			if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary) continue;
			if (method.AssociatedSymbol is not null) continue;
			if (method.DeclaredAccessibility != Accessibility.Public) continue;
			if (defaultCommand is not null && SymbolEqualityComparer.Default.Equals(method, defaultCommand)) continue;
			var cmdName = Naming.ToCommandName(method.Name);
			targetNode.Commands.Add(CommandModel.FromMethod(cmdName, method, parseOpts, routePrefix, acc, location));
		}
	}

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

	private static OptionsTypeModel? BuildOptionsTypeModel(INamedTypeSymbol type)
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
					members.Add(ParameterModel.FromOptionsProperty(prop));
					break;
				}
				case IFieldSymbol field when field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic:
					members.Add(ParameterModel.FromOptionsField(field));
					break;
			}
		}

		var typeFq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var typeMetaName = GetMetadataNameStatic(type);
		var baseNames = CollectBaseTypeMetadataNames(type);
		var flattenedMembers = BuildFlattenedOptionsMembers(type);
		var bestCtorParamOrder = ComputeBestCtorParamOrder(type, members.Count > 0 ? members.ToImmutable() : ImmutableArray<ParameterModel>.Empty);
		var isPublic = type.DeclaredAccessibility == Accessibility.Public;
		var isGeneric = type.TypeParameters.Length > 0;

		if (members.Count == 0)
			return new OptionsTypeModel(typeFq, typeMetaName, baseNames, ImmutableArray<ParameterModel>.Empty, flattenedMembers, bestCtorParamOrder, isPublic, isGeneric);

		return new OptionsTypeModel(typeFq, typeMetaName, baseNames, members.ToImmutable(), flattenedMembers, bestCtorParamOrder, isPublic, isGeneric);
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

	private static IMethodSymbol? ResolveHandlerMethod(
		SemanticModel model,
		ExpressionSyntax handlerExpr,
		SourceProductionContext context,
		InvocationExpressionSyntax invocation)
	{
		var symbol = model.GetSymbolInfo(handlerExpr).Symbol;
		switch (symbol)
		{
			case IMethodSymbol m:
				return m;
			case IFieldSymbol { IsStatic: true, ConstantValue: { } }:
				context.ReportDiagnostic(Diagnostic.Create(HandlerMustBeMethod, handlerExpr.GetLocation()));
				return null;
		}

		var op = model.GetOperation(handlerExpr);
		while (op is IConversionOperation conv)
			op = conv.Operand;

		if (op is IMethodReferenceOperation directRef)
			return directRef.Method;

		if (op is IDelegateCreationOperation del && del.Target is IMethodReferenceOperation reference)
			return reference.Method;

		context.ReportDiagnostic(Diagnostic.Create(HandlerMustBeMethod, handlerExpr.GetLocation()));
		return null;
	}


	private static bool IsInjected(IParameterSymbol p)
	{
		var t = p.Type;
		if (t is INamedTypeSymbol named && named.TypeKind == TypeKind.Struct)
		{
			var fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (fq == "global::System.Threading.CancellationToken")
				return true;
		}

		return false;
	}

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

	private static void ReportDuplicateCliNames(SourceProductionContext context, Location location, ImmutableArray<ParameterModel> parameters)
	{
		var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var p in parameters)
		{
			if (p.Kind != ParameterKind.Flag)
				continue;
			void check(string name)
			{
				if (string.IsNullOrEmpty(name))
					return;
				if (seen.TryGetValue(name, out var first))
				{
					if (!string.Equals(first, p.SymbolName, StringComparison.Ordinal))
						context.ReportDiagnostic(Diagnostic.Create(DuplicateCliNames, location, name));
				}
				else
				{
					seen[name] = p.SymbolName;
				}
			}

			check(p.CliLongName);
			foreach (var al in p.Aliases)
				check(al);
			if (p.Special == BoolSpecialKind.NullableBool)
				check("no-" + p.CliLongName);
		}
	}

	private static void ValidateExpandedParameterLayout(SourceProductionContext context, Location location, ImmutableArray<ParameterModel> expanded)
	{
		var seenFlag = false;
		foreach (var p in expanded)
		{
			if (p.Kind == ParameterKind.Injected)
				continue;
			if (p.Kind == ParameterKind.Flag)
			{
				seenFlag = true;
				continue;
			}

			if (p.Kind == ParameterKind.Positional && seenFlag)
			{
				context.ReportDiagnostic(Diagnostic.Create(ArgumentOrder, location));
				return;
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

	/// <summary>DiagnosticAccumulator-based overload for Select-step analysis.</summary>
	private static void ValidateExpandedParameterLayoutAcc(DiagnosticAccumulator acc, Location location, ImmutableArray<ParameterModel> expanded)
	{
		var seenFlag = false;
		foreach (var p in expanded)
		{
			if (p.Kind == ParameterKind.Injected) continue;
			if (p.Kind == ParameterKind.Flag) { seenFlag = true; continue; }
			if (p.Kind == ParameterKind.Positional && seenFlag)
			{
				acc.Add(ArgumentOrder, location);
				return;
			}
		}
	}

	/// <summary>DiagnosticAccumulator-based overload for Select-step analysis.</summary>
	private static ImmutableArray<ParameterModel> FlattenAsParametersTypeAcc(
		DiagnosticAccumulator acc,
		Location location,
		IParameterSymbol methodParam,
		INamedTypeSymbol type,
		string? prefix,
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
				list.Add(ParameterModel.FromAsParametersCtorParameter(owner, typeFq, type, cp, pfx, order++, parseOptions));
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
				list.Add(ParameterModel.FromAsParametersInitProperty(methodParamName: owner, typeFq, prop, pfx, order++, parseOptions));
			}
		}
		if (list.Count == 0)
			acc.Add(AsParametersEmptyType, location, type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat));
		return list.ToImmutableArray();
	}

	private static ImmutableArray<ParameterModel> FlattenAsParametersType(
		SourceProductionContext context,
		Location location,
		HandlerParam handlerParam,
		INamedTypeSymbol type,
		string? prefix,
		CSharpParseOptions parseOptions)
	{
		return FlattenAsParametersType(context, location, handlerParam.Name, type, prefix, parseOptions);
	}

	private static ImmutableArray<ParameterModel> FlattenAsParametersType(
		SourceProductionContext context,
		Location location,
		IParameterSymbol methodParam,
		INamedTypeSymbol type,
		string? prefix,
		CSharpParseOptions parseOptions)
	{
		return FlattenAsParametersType(context, location, methodParam.Name, type, prefix, parseOptions);
	}

	private static ImmutableArray<ParameterModel> FlattenAsParametersType(
		SourceProductionContext context,
		Location location,
		string methodParamName,
		INamedTypeSymbol type,
		string? prefix,
		CSharpParseOptions parseOptions)
	{
		var pfx = string.IsNullOrWhiteSpace(prefix) ? "" : Naming.ToCliLongName(prefix!.Trim()) + "-";
		var owner = methodParamName;
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
				list.Add(ParameterModel.FromAsParametersCtorParameter(
					owner,
					typeFq,
					type,
					cp,
					pfx,
					order++,
					parseOptions));
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
				if (member is not IPropertySymbol prop)
					continue;
				if (prop.DeclaredAccessibility != Accessibility.Public || prop.IsStatic)
					continue;
				if (prop.IsIndexer)
					continue;
				if (!IsSettableForAsParameters(prop))
					continue;
				if (ctorNames.Contains(prop.Name))
					continue;
				if (!seenPropNames.Add(prop.Name))
					continue;

				list.Add(ParameterModel.FromAsParametersInitProperty(
					methodParamName: owner,
					typeFq,
					prop,
					pfx,
					order++,
					parseOptions));
			}
		}

		if (list.Count == 0)
			context.ReportDiagnostic(Diagnostic.Create(AsParametersEmptyType, location, type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)));

		return list.ToImmutableArray();
	}

	private static string? TryGetStringLiteral(ExpressionSyntax expr) =>
		expr switch
		{
			LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } lit => lit.Token.ValueText,
			_ => null
		};

	private static string? TryGetStringConstant(SemanticModel model, ExpressionSyntax expr)
	{
		var lit = TryGetStringLiteral(expr);
		if (lit is not null)
			return lit;
		var cv = model.GetConstantValue(expr);
		if (cv.HasValue && cv.Value is string s)
			return s;
		return null;
	}

	private static void EmitNamespaceSegmentCodegen(SourceProductionContext context, AppEmitModel app)
	{
		if (app.ArglessNamespaceCodegen.Count == 0)
			return;

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated />");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using Nullean.Argh;");
		sb.AppendLine();
		sb.AppendLine("internal static class ArghNamespaceSegmentInitializer");
		sb.AppendLine("{");
		sb.AppendLine("	[System.Runtime.CompilerServices.ModuleInitializer]");
		sb.AppendLine("	internal static void Init()");
		sb.AppendLine("	{");
		foreach (var e in app.ArglessNamespaceCodegen)
		{
			var escaped = e.Segment.Replace("\\", "\\\\").Replace("\"", "\\\"");
			sb.AppendLine("\t\tglobal::Nullean.Argh.ArghNamespaceSegmentCodegen.Set<" + e.TypeFq + ">(\"" + escaped + "\");");
		}
		sb.AppendLine("	}");
		sb.AppendLine("}");
		context.AddSource("ArghNamespaceSegmentInitializer.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}


	private static void EmitEmpty(SourceProductionContext context, string assemblyName, string assemblyVersion)
	{
		const string source = """
			// <auto-generated/>
			#nullable enable
			using System;
			using System.Threading.Tasks;
			using Nullean.Argh.Middleware;
			using Nullean.Argh.Help;
			using Nullean.Argh.Matching;
			using Nullean.Argh.Runtime;
			using Nullean.Argh.Schema;

			namespace Nullean.Argh
			{
				/// <summary>Source-generated CLI entry point from <c>ArghApp</c> registrations. At the root, <c>__completion bash|zsh|fish</c> prints a shell script from <see cref="global::Nullean.Argh.Help.CompletionScriptTemplates"/>.</summary>
				public static class ArghGenerated
				{
					public static Task<int> RunAsync(string[] args) =>
						Task.FromResult(Run(args));

					public static bool TryParseRoute(string[] args, out RouteMatch match)
					{
						match = default;
						if (CompletionProtocol.IsArghMetaCompletionInvocation(args))
							return false;
						return false;
					}

					public static RouteMatch? Route(string[] args)
					{
						if (args is null)
							throw new ArgumentNullException(nameof(args));
						if (!TryParseRoute(args, out var m))
							return null;
						return m;
					}

					private static int Run(string[] args)
					{
						if (CompletionProtocol.IsCompletionScriptInvocation(args))
						{
							if (!CompletionProtocol.TryParseCompletionScriptInvocation(args, out var __scriptShell))
							{
								System.Console.Error.WriteLine("Error: expected '__completion bash|zsh|fish'");
								return 2;
							}
							var appName = "__ARGH_EMBED_ASM_NAME__";
							switch (__scriptShell)
							{
								case CompletionShell.Bash:
									System.Console.Out.Write(CompletionScriptTemplates.GetBash().Replace("{0}", appName));
									return 0;
								case CompletionShell.Zsh:
									System.Console.Out.Write(CompletionScriptTemplates.GetZsh().Replace("{0}", appName));
									return 0;
								case CompletionShell.Fish:
									System.Console.Out.Write(CompletionScriptTemplates.GetFish().Replace("{0}", appName));
									return 0;
								default:
									return 2;
							}
						}

						if (CompletionProtocol.IsCompleteInvocation(args))
						{
							if (!CompletionProtocol.TryParseCompleteInvocation(args, out _, out var __words))
							{
								System.Console.Error.WriteLine("Error: expected '__complete <bash|zsh|fish> -- [words...]'");
								return 2;
							}
							Complete(default, __words);
							return 0;
						}

						if (CompletionProtocol.IsSchemaInvocation(args))
						{
							System.Console.Out.Write(ArghRuntime.FormatCliSchemaJson());
							return 0;
						}

						if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
						{
							System.Console.Out.WriteLine("No commands are registered.");
							return 0;
						}

						if (args.Length > 0 && args[0] == "--version")
						{
							PrintVersion();
							return 0;
						}

						System.Console.Error.WriteLine("No commands are registered.");
						return 2;
					}

					private static void Complete(CompletionShell shell, ReadOnlySpan<string> words)
					{
						_ = shell;
						_ = words;
					}

					private static void PrintVersion()
					{
						System.Console.Out.WriteLine("__ARGH_EMBED_ASM_VER__");
					}

					internal static ArghCliSchemaDocument BuildCliSchemaDocument() =>
						new ArghCliSchemaDocument(
							1,
							"__ARGH_EMBED_ASM_NAME__",
							"__ARGH_EMBED_ASM_VER__",
							new[] { "__complete", "__completion", "__schema" },
							Array.Empty<CliParameterSchema>(),
							null,
							Array.Empty<CliCommandSchema>(),
							Array.Empty<CliNamespaceSchema>());
				}

				internal static class ArghGeneratedRuntimeRegistration
				{
					[System.Runtime.CompilerServices.ModuleInitializer]
					internal static void RegisterArghRuntime()
					{
						ArghRuntime.RegisterRunner(ArghGenerated.RunAsync);
						ArghRuntime.RegisterRoute(ArghGenerated.Route);
						ArghRuntime.RegisterCliSchema(ArghGenerated.BuildCliSchemaDocument);
					}
				}
			}
			""";
		var resolved = source
			.Replace("__ARGH_EMBED_ASM_NAME__", Escape(assemblyName))
			.Replace("__ARGH_EMBED_ASM_VER__", Escape(assemblyVersion));
		context.AddSource("ArghGenerated.g.cs", SourceText.From(resolved, Encoding.UTF8));
	}


	private static void AppendArghRuntimeModuleInitializer(StringBuilder sb)
	{
		sb.AppendLine();
		sb.AppendLine("\tinternal static class ArghGeneratedRuntimeRegistration");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\t[System.Runtime.CompilerServices.ModuleInitializer]");
		sb.AppendLine("\t\tinternal static void RegisterArghRuntime()");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tArghRuntime.RegisterRunner(ArghGenerated.RunAsync);");
		sb.AppendLine("\t\t\tArghRuntime.RegisterRoute(ArghGenerated.Route);");
		sb.AppendLine("\t\t\tArghRuntime.RegisterCliSchema(ArghGenerated.BuildCliSchemaDocument);");
		sb.AppendLine("\t\t}");
		sb.AppendLine("\t}");
	}

	private const int FuzzyMaxDistance = 2;

	private static void EmitRootCompletionScriptBlock(StringBuilder sb, string indent, string entryAssemblyName)
	{
		sb.AppendLine(indent + "if (CompletionProtocol.IsCompletionScriptInvocation(args))");
		sb.AppendLine(indent + "{");
		sb.AppendLine(indent + "\tif (!CompletionProtocol.TryParseCompletionScriptInvocation(args, out var __scriptShell))");
		sb.AppendLine(indent + "\t{");
		sb.AppendLine(indent + "\t\tConsole.Error.WriteLine(\"Error: expected '__completion bash|zsh|fish'\");");
		sb.AppendLine(indent + "\t\treturn 2;");
		sb.AppendLine(indent + "\t}");
		sb.AppendLine(indent + "\tvar __entry = \"" + Escape(entryAssemblyName) + "\";");
		sb.AppendLine(indent + "\tswitch (__scriptShell)");
		sb.AppendLine(indent + "\t{");
		sb.AppendLine(indent + "\t\tcase CompletionShell.Bash:");
		sb.AppendLine(indent + "\t\t\tConsole.Out.Write(CompletionScriptTemplates.GetBash().Replace(\"{0}\", __entry));");
		sb.AppendLine(indent + "\t\t\treturn 0;");
		sb.AppendLine(indent + "\t\tcase CompletionShell.Zsh:");
		sb.AppendLine(indent + "\t\t\tConsole.Out.Write(CompletionScriptTemplates.GetZsh().Replace(\"{0}\", __entry));");
		sb.AppendLine(indent + "\t\t\treturn 0;");
		sb.AppendLine(indent + "\t\tcase CompletionShell.Fish:");
		sb.AppendLine(indent + "\t\t\tConsole.Out.Write(CompletionScriptTemplates.GetFish().Replace(\"{0}\", __entry));");
		sb.AppendLine(indent + "\t\t\treturn 0;");
		sb.AppendLine(indent + "\t\tdefault:");
		sb.AppendLine(indent + "\t\t\treturn 2;");
		sb.AppendLine(indent + "\t}");
		sb.AppendLine(indent + "}");
		sb.AppendLine();
	}


	private static void EmitFuzzyDispatchDefault(
		StringBuilder sb,
		RegistryNode node,
		ImmutableArray<string> path,
		string entryAssemblyName)
	{
		var entries = new List<(string Name, string Summary, string HelpPrinter)>();
		foreach (var cmd in node.Commands)
			entries.Add((cmd.CommandName, cmd.SummaryOneLiner, $"PrintHelp_{cmd.RunMethodName}"));
		foreach (var ch in node.Children)
		{
			var childPath = AppendSegment(path, ch.Segment);
			var gk = CommandNamespacePathKey(childPath);
			entries.Add((ch.Segment, "", $"PrintHelp_CommandNamespace_{gk}"));
		}

		var sorted =
			entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();

		var pathPrefix = path.IsDefaultOrEmpty ? "" : string.Join(" ", path) + " ";
		var nsHelp = path.IsDefaultOrEmpty ? "--help" : Escape(string.Join(" ", path)) + " --help";

		sb.AppendLine("\t\t\t\tvar __tok = tok;");
		sb.AppendLine("\t\t\t\tvar __app = \"" + Escape(entryAssemblyName) + "\";");
		sb.Append("\t\t\t\tvar __cands = new string[] { ");
		for (var i = 0; i < sorted.Count; i++)
		{
			if (i > 0)
				sb.Append(", ");
			sb.Append('"').Append(Escape(sorted[i].Name)).Append('"');
		}

		sb.AppendLine(" };");
		sb.AppendLine($"\t\t\t\tvar __matches = FuzzyMatch.FindClosest(__tok, __cands, {FuzzyMaxDistance});");
		const string kind = "command or namespace";
		sb.AppendLine("\t\t\t\tif (__matches.Count == 0)");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown {kind} '{{__tok}}'.\");");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\telse if (__matches.Count == 1)");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\tvar __m = __matches[0];");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown {kind} '{{__tok}}'. Did you mean '{Escape(pathPrefix)}{{__m}}'?\");");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Run '{{__app}} {Escape(pathPrefix)}{{__m}} --help' for usage.\");");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Run '{{__app}} {nsHelp}' for usage.\");");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\telse");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown {kind} '{{__tok}}'. Did you mean one of these?\");");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
		foreach (var e in sorted)
		{
			var qualifiedName = Escape(pathPrefix + e.Name);
			var sum = Escape(e.Summary);
			sb.AppendLine(
				$"\t\t\t\t\tif (__matches.Any(__x => string.Equals(__x, \"{Escape(e.Name)}\", StringComparison.OrdinalIgnoreCase)))");
			sb.AppendLine("\t\t\t\t\t{");
			sb.AppendLine(
				$"\t\t\t\t\t\tConsole.Error.WriteLine(\"  \" + CliHelpFormatting.Accent(\"{qualifiedName}\") + \"    {sum}\");");
			sb.AppendLine("\t\t\t\t\t}");
		}

		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Run '{{__app}} {Escape(pathPrefix)}<command> --help' for usage.\");");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Run '{{__app}} {nsHelp}' for usage.\");");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\treturn 2;");
	}


	private static void AppendRunWithCancellationAsyncMethod(StringBuilder sb)
	{
		sb.AppendLine("\t\tprivate static async Task<int> RunWithCancellationAsync(string[] args)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tusing var cts = new CancellationTokenSource();");
		sb.AppendLine("\t\t\tConsole.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };");
		sb.AppendLine("\t\t\tCancellationTokenSource? __linkedCts = null;");
		sb.AppendLine("\t\t\ttry");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tCancellationToken ct = cts.Token;");
		sb.AppendLine("\t\t\t\tif (ArghHostRuntime.ApplicationStopping is CancellationToken __hostStopping)");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t__linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, __hostStopping);");
		sb.AppendLine("\t\t\t\t\tct = __linkedCts.Token;");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\treturn await RunCoreAsync(args, ct).ConfigureAwait(false);");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\tcatch (Exception ex)");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tConsole.Error.WriteLine(ex.ToString());");
		sb.AppendLine("\t\t\t\treturn 1;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\tfinally");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\t__linkedCts?.Dispose();");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	/// <summary>
	/// Returns <see langword="true"/> if the method or its containing type carries
	/// <c>[NoOptionsInjection]</c>, suppressing AGH0021 and all options-injection codegen.
	/// </summary>
	private static bool HasNoOptionsInjection(IMethodSymbol method)
	{
		const string attrName = "NoOptionsInjectionAttribute";
		const string ns = "Nullean.Argh";
		foreach (var a in method.GetAttributes())
			if (a.AttributeClass?.Name == attrName && a.AttributeClass.ContainingNamespace?.ToDisplayString() == ns)
				return true;
		foreach (var a in method.ContainingType.GetAttributes())
			if (a.AttributeClass?.Name == attrName && a.AttributeClass.ContainingNamespace?.ToDisplayString() == ns)
				return true;
		return false;
	}

	private static bool HasPublicParameterlessCtor(INamedTypeSymbol type)
	{
		foreach (var ctor in type.InstanceConstructors)
		{
			if (ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public)
				return true;
		}

		return false;
	}

	private static string DiResolveOrNew(string fullyQualifiedType, bool allowParameterlessFallback)
	{
		if (allowParameterlessFallback)
			return $"((ArghServices.ServiceProvider?.GetService(typeof({fullyQualifiedType})) as {fullyQualifiedType}) ?? new {fullyQualifiedType}())";

		return
			$"((ArghServices.ServiceProvider?.GetService(typeof({fullyQualifiedType})) as {fullyQualifiedType}) ?? throw new global::System.InvalidOperationException(\"Register the type in DI for hosted execution, or add a public parameterless constructor for standalone CLI.\"))";
	}

	private static void EmitApp(
		SourceProductionContext context,
		AppEmitModel app,
		CSharpParseOptions parseOptions,
		string entryAssemblyName,
		string entryAssemblyVersion,
		ReferenceMetadataCapabilities.Capabilities referenceCapabilities)
	{
		_ = referenceCapabilities;
		_ = parseOptions;  // no longer needed for DTO building; kept in signature for future use
		var dtoTargets = CollectDtoBindingTargets(app);
		EmitHierarchical(context, app, dtoTargets, entryAssemblyName, entryAssemblyVersion);
		EmitDtoTypeExtensions(context, dtoTargets);
	}

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

	private static ImmutableArray<ParameterModel> BuildFlattenedOptionsMembers(INamedTypeSymbol type)
	{
		var chain = new List<INamedTypeSymbol>();
		for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
			chain.Add(t);

		var members = ImmutableArray.CreateBuilder<ParameterModel>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
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

						members.Add(ParameterModel.FromOptionsProperty(prop));
						break;
					}
					case IFieldSymbol field when field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic:
					{
						if (!seen.Add(field.Name))
							continue;

						members.Add(ParameterModel.FromOptionsField(field));
						break;
					}
				}
			}
		}

		return members.ToImmutable();
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
			var methodName = "TryParseDto_" + DtoMethodSuffix(t.TypeFq);
			var syn = SyntheticOptionsCommand(t.Members, methodName);
			EmitCommandRunner(
				sb,
				syn,
				ImmutableArray<GlobalMiddlewareRegistration>.Empty,
				emitDtoTryParse: true,
				dtoMethodName: methodName,
				dtoResultTypeFq: t.TypeFq,
				dtoOptionsTypeFq: t.IsOptionsDto ? t.TypeFq : null,
				dtoOptionsBestCtorParamOrder: t.IsOptionsDto ? t.BestCtorParamOrder : null);
		}
	}

	private static void EmitDtoTypeExtensions(SourceProductionContext context, ImmutableArray<DtoBindingTarget> targets)
	{
		if (targets.IsEmpty)
			return;

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using System;");
		sb.AppendLine();
		sb.AppendLine("namespace Nullean.Argh");
		sb.AppendLine("{");
		sb.AppendLine("\t/// <summary>Source-generated DTO parsers. Uses C# 14 extension members (static extensions on each DTO type) plus a <see cref=\"Type\"/>-based overload for generic dispatch.</summary>");
		sb.AppendLine("\tpublic static class ArghTypeBindingExtensions");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tpublic static bool ArghTryParse<T>(this Type type, string[] args, out T? value) where T : class");
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
			sb.AppendLine($"\t\t\t\tvar ok = ArghGenerated.{method}(args, out var v);");
			sb.AppendLine("\t\t\t\tvalue = (T?)(object?)v;");
			sb.AppendLine("\t\t\t\treturn ok;");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine(
			"\t\t\tthrow new InvalidOperationException(\"No pregenerated Argh DTO parser for \" + typeof(T).FullName + \". Register the type as GlobalOptions/CommandNamespaceOptions or use it with [AsParameters] on a command.\");");
		sb.AppendLine("\t\t}");
		sb.AppendLine();

		foreach (var t in targets)
		{
			if (t.IsGeneric)
				continue;

			var fq = t.TypeFq;
			var method = "TryParseDto_" + DtoMethodSuffix(fq);
			var vis = t.IsPublic ? "public" : "internal";
			sb.AppendLine($"\t\textension({fq})");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\t{vis} static bool ArghTryParse(string[] args, out {fq}? value) =>");
			sb.AppendLine($"\t\t\t\tglobal::Nullean.Argh.ArghGenerated.{method}(args, out value);");
			sb.AppendLine("\t\t}");
			sb.AppendLine();
		}

		sb.AppendLine("\t}");
		sb.AppendLine("}");
		context.AddSource("ArghTypeBindingExtensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}



	private static void EmitHierarchical(SourceProductionContext context, AppEmitModel app, ImmutableArray<DtoBindingTarget> dtoTargets, string entryAssemblyName, string entryAssemblyVersion)
	{
		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using System;");
		sb.AppendLine("using System.Collections.Generic;");
		sb.AppendLine("using System.Globalization;");
		sb.AppendLine("using System.IO;");
		sb.AppendLine("using System.Linq;");
		sb.AppendLine("using System.Threading;");
		sb.AppendLine("using System.Threading.Tasks;");
		sb.AppendLine("using Nullean.Argh.Middleware;");
		sb.AppendLine("using Nullean.Argh.Help;");
		sb.AppendLine("using Nullean.Argh.Matching;");
		sb.AppendLine("using Nullean.Argh.Runtime;");
		sb.AppendLine("using Nullean.Argh.Schema;");
		sb.AppendLine();
		sb.AppendLine("namespace Nullean.Argh");
		sb.AppendLine("{");
		sb.AppendLine("\t/// <summary>Source-generated CLI entry point from <c>ArghApp</c> registrations. At the root, <c>__completion bash|zsh|fish</c> prints a shell script from <see cref=\"global::Nullean.Argh.Help.CompletionScriptTemplates\"/>; each <c>{0}</c> in the template is replaced with the entry assembly name (same effect as <c>string.Format</c>, but substitution uses <c>Replace</c> so shell scripts can contain literal braces).</summary>");
		sb.AppendLine("\tpublic static class ArghGenerated");
		sb.AppendLine("\t{");

		// Static fields that hold the parsed global/namespace options instances.
		// Commands inject these via method or constructor parameters.
		if (app.GlobalOptionsModel is { } globalOptModel)
		{
			var fq = globalOptModel.TypeFq;
			sb.AppendLine($"\t\tprivate static {fq} {OptionsStaticFieldNameFq(fq)} = new {fq}();");
		}
		foreach ((var nsNode, _) in EnumerateCommandNamespaceNodesWithPath(app.Root, ImmutableArray<string>.Empty))
		{
			if (nsNode.CommandNamespaceOptionsModel is { } nsModel)
			{
				var fq = nsModel.TypeFq;
				sb.AppendLine($"\t\tprivate static {fq} {OptionsStaticFieldNameFq(fq)} = new {fq}();");
			}
		}
		sb.AppendLine();

		EmitCompletionForApp(sb, app);
		sb.AppendLine("\t\tpublic static Task<int> RunAsync(string[] args) =>");
		sb.AppendLine("\t\t\tRunWithCancellationAsync(args);");
		sb.AppendLine();
		AppendRunWithCancellationAsyncMethod(sb);
		EmitRunCoreHierarchical(sb, app, entryAssemblyName);
		sb.AppendLine();
		EmitPrintRootHelpHierarchical(sb, app, entryAssemblyName);
		sb.AppendLine();
		foreach ((var node, var path) in EnumerateCommandNamespaceNodesWithPath(app.Root, ImmutableArray<string>.Empty))
			EmitCommandNamespaceHelpPrinter(sb, path, node, app, entryAssemblyName);

		foreach (var cmd in app.AllCommands)
			EmitCommandHelpPrinter(sb, cmd, app, entryAssemblyName);

		sb.AppendLine("\t\tprivate static void PrintVersion()");
		sb.AppendLine("\t\t{");
		sb.AppendLine($"			Console.Out.WriteLine(\"{Escape(entryAssemblyVersion)}\");");
		sb.AppendLine("\t\t}");
		sb.AppendLine();

		foreach (var cmd in app.AllCommands)
		{
			var injectedOpts = cmd.HandlerHasNoOptionsInjection
				? ImmutableArray<(string, string, ImmutableArray<string>, string, string, ImmutableArray<ParameterModel>, ImmutableArray<string>?)>.Empty
				: app.InjectionChains.TryGetValue(cmd.RunMethodName, out var precomputed3)
					? precomputed3
					: BuildOptionsInjectionChain(app, cmd);
			EmitCommandRunner(sb, cmd, app.GlobalMiddleware, injectedOptions: injectedOpts);
		}

		if (app.GlobalOptionsModel is { Members: { Length: > 0 } } gomStore)
			EmitOptionsTryParse(sb, "TryParseGlobalOptions", gomStore.FlattenedMembers,
				storeTypeFq: gomStore.TypeFq,
				storeFieldName: OptionsStaticFieldNameFq(gomStore.TypeFq),
				storeBestCtorParamOrder: gomStore.BestCtorParamOrder);

		foreach ((var node, var path) in EnumerateCommandNamespaceNodesWithPath(app.Root, ImmutableArray<string>.Empty))
		{
			if (node.CommandNamespaceOptionsModel is { } nsModel)
			{
				// Use FLATTENED members (including inherited) so flags from parent options types are
				// also recognised and consumed between the namespace segment and the sub-command.
				if (nsModel.FlattenedMembers.Length > 0)
					EmitOptionsTryParse(sb, CommandNamespaceOptionsParseMethodName(path), nsModel.FlattenedMembers,
						storeTypeFq: nsModel.TypeFq,
						storeFieldName: OptionsStaticFieldNameFq(nsModel.TypeFq),
						storeBestCtorParamOrder: nsModel.BestCtorParamOrder);
			}
		}

		EmitTryParseRouteHierarchical(sb, app);
		EmitDispatchForNode(sb, app, app.Root, ImmutableArray<string>.Empty, "DispatchRoot", isRoot: true, entryAssemblyName);
		sb.AppendLine("\t\tprivate static bool? ParseNullableBool(string? raw, bool fromYesSwitch)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tif (string.IsNullOrEmpty(raw)) return fromYesSwitch;");
		sb.AppendLine("\t\t\tif (bool.TryParse(raw, out var b)) return b;");
		sb.AppendLine("\t\t\treturn fromYesSwitch;");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		sb.AppendLine("\t\tprivate static string[] TailFrom(string[] args, int start)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tif (start >= args.Length) return Array.Empty<string>();");
		sb.AppendLine("\t\t\tvar n = args.Length - start;");
		sb.AppendLine("\t\t\tvar r = new string[n];");
		sb.AppendLine("\t\t\tArray.Copy(args, start, r, 0, n);");
		sb.AppendLine("\t\t\treturn r;");
		sb.AppendLine("\t\t}");
		EmitDtoBindingMethods(sb, dtoTargets);
		sb.AppendLine();
		EmitBuildCliSchemaDocumentHierarchical(sb, app, entryAssemblyName, entryAssemblyVersion);
		sb.AppendLine("\t}");
		AppendArghRuntimeModuleInitializer(sb);
		sb.AppendLine("}");
		context.AddSource("ArghGenerated.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}

	private static IEnumerable<(RegistryNode node, ImmutableArray<string> path)> EnumerateCommandNamespaceNodesWithPath(
		RegistryNode root,
		ImmutableArray<string> prefix)
	{
		foreach (var ch in root.Children)
		{
			var p = AppendSegment(prefix, ch.Segment);
			yield return (ch.Node, p);
			foreach ((var node, var sub) in EnumerateCommandNamespaceNodesWithPath(ch.Node, p))
				yield return (node, sub);
		}
	}

	private static string CommandNamespacePathKey(ImmutableArray<string> path)
	{
		if (path.IsDefaultOrEmpty)
			return "Root";

		var sb = new StringBuilder();
		for (var i = 0; i < path.Length; i++)
		{
			if (i > 0)
				sb.Append('_');
			sb.Append(Naming.SanitizeIdentifier(path[i]));
		}

		return sb.ToString();
	}

	private static string CommandNamespaceOptionsParseMethodName(ImmutableArray<string> path) =>
		"TryParseCommandNamespaceOptions_" + CommandNamespacePathKey(path);

	private static void EmitRunCoreHierarchical(StringBuilder sb, AppEmitModel app, string entryAssemblyName)
	{
		var hasGlobal = app.GlobalOptionsModel is { Members: { Length: > 0 } };
		sb.AppendLine("\t\tprivate static async Task<int> RunCoreAsync(string[] args, CancellationToken ct)");
		sb.AppendLine("\t\t{");
		EmitRootCompletionScriptBlock(sb, "\t\t\t", entryAssemblyName);
		EmitRootCompleteBlock(sb, "\t\t\t");
		EmitRootSchemaBlock(sb, "\t\t\t");
		sb.AppendLine("\t\t\tvar idx = new int[1];");
		if (hasGlobal)
			sb.AppendLine("\t\t\tif (!TryParseGlobalOptions(args, idx)) return 2;");

		sb.AppendLine("\t\t\tif (idx[0] < args.Length && (args[idx[0]] == \"--help\" || args[idx[0]] == \"-h\"))");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tPrintRootHelp();");
		sb.AppendLine("\t\t\t\treturn 0;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\tif (idx[0] < args.Length && args[idx[0]] == \"--version\")");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tPrintVersion();");
		sb.AppendLine("\t\t\t\treturn 0;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\tif (idx[0] >= args.Length)");
		sb.AppendLine("\t\t\t{");
		if (app.Root.RootCommand is { } runCoreRoot)
			sb.AppendLine($"\t\t\t\treturn await {runCoreRoot.RunMethodName}(TailFrom(args, idx[0]), ct).ConfigureAwait(false);");
		else
		{
			sb.AppendLine("\t\t\t\tPrintRootHelp();");
			sb.AppendLine("\t\t\t\treturn 0;");
		}

		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\treturn await DispatchRoot(args, idx, ct).ConfigureAwait(false);");
		sb.AppendLine("\t\t}");
	}

	private static void EmitPrintRootHelpHierarchical(StringBuilder sb, AppEmitModel app, string entryAssemblyName)
	{
		var rootGlobalFlags = new List<ParameterModel>();
		if (app.Root.RootCommand is not null && app.GlobalOptionsModel is OptionsTypeModel gomH && gomH.Members.Length > 0)
		{
			foreach (var p in gomH.Members)
			{
				if (p.Kind == ParameterKind.Flag)
					rootGlobalFlags.Add(p);
			}
		}

		var widthCandidatesGlobalRoot = new List<int> { "--help, -h".Length, "--version".Length };
		widthCandidatesGlobalRoot.AddRange(rootGlobalFlags.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		var maxOptWidthRoot = Math.Min(widthCandidatesGlobalRoot.Max(), 40);
		maxOptWidthRoot = Math.Max(maxOptWidthRoot, "--help, -h".Length);

		var maxNsListingW = app.Root.Children.Count == 0 ? 0 : app.Root.Children.Max(ch => ch.Segment.Length);
		var maxCmdListingW = app.Root.Commands.Count == 0 ? 0 : app.Root.Commands.Max(c => c.CommandName.Length);

		sb.AppendLine("\t\tprivate static void PrintRootHelp()");
		sb.AppendLine("\t\t{");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Usage: \") + CliHelpFormatting.Accent(\"{Escape(entryAssemblyName)}\") + \" <namespace|command> [options]\");");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		if (app.Root.RootCommand is { } rootOverview)
			EmitRootCommandHelpOverview(sb, rootOverview, "\t\t\t", app, entryAssemblyName);

		sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Global options:\"));");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(\"  \" + CliHelpFormatting.Placeholder(\"{Escape("--help, -h".PadRight(maxOptWidthRoot))}\") + \"  Show help.\");");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(\"  \" + CliHelpFormatting.Placeholder(\"{Escape("--version".PadRight(maxOptWidthRoot))}\") + \"  Show version.\");");
		EmitHelpOptionRows(sb, rootGlobalFlags, maxOptWidthRoot);
		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		if (app.Root.Children.Count > 0)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Namespaces:\"));");
			foreach (var ch in app.Root.Children)
			{
				var sumArg = string.IsNullOrWhiteSpace(ch.SummaryOneLiner)
					? "null"
					: $"\"{Escape(ch.SummaryOneLiner)}\"";
				sb.AppendLine(
					$"\t\t\tCliHelpFormatting.WriteHelpListNameAndDescription(true, \"{Escape(ch.Segment)}\", {sumArg}, {maxNsListingW});");
			}

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		if (app.Root.Commands.Count > 0)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Commands:\"));");
			foreach (var c in app.Root.Commands)
			{
				var sumArg = string.IsNullOrWhiteSpace(c.SummaryOneLiner)
					? "null"
					: $"\"{Escape(c.SummaryOneLiner)}\"";
				sb.AppendLine(
					$"\t\t\tCliHelpFormatting.WriteHelpListNameAndDescription(false, \"{Escape(c.CommandName)}\", {sumArg}, {maxCmdListingW});");
			}
		}

		sb.AppendLine("\t\t}");
	}

	private static void EmitCommandNamespaceHelpPrinter(StringBuilder sb, ImmutableArray<string> path, RegistryNode node, AppEmitModel app, string entryAssemblyName)
	{
		var key = CommandNamespacePathKey(path);
		var usagePrefix = string.Join(" ", path);

		var globalFlagMembers = EnumerateFlagMembers(app.GlobalOptionsModel).ToList();
		List<(string Segment, List<ParameterModel> Rows)> namespaceOptionSections = new();
		var namespaceOptionChain = GetCommandNamespaceOptionChain(app, path);
		var suppressedForNamespaceDisplay = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddCliKeys(globalFlagMembers, suppressedForNamespaceDisplay);
		foreach ((var seg, var gom) in namespaceOptionChain)
		{
			var allInNamespace = EnumerateFlagMembers(gom).ToList();
			var rows = allInNamespace.Where(p => !suppressedForNamespaceDisplay.Contains(p.CliLongName)).ToList();
			AddCliKeys(allInNamespace, suppressedForNamespaceDisplay);
			if (rows.Count > 0)
				namespaceOptionSections.Add((seg, rows));
		}

		var hasNsDoc = !string.IsNullOrWhiteSpace(node.SummaryInnerXml);
		var showGlobalFlagsInNs = (node.RootCommand is not null || hasNsDoc) && globalFlagMembers.Count > 0;
		var showNamespaceScopedOptionSections = node.RootCommand is not null || hasNsDoc;
		var widthCandidatesNs = new List<int> { "--help, -h".Length };
		if (showGlobalFlagsInNs)
			widthCandidatesNs.AddRange(globalFlagMembers.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		if (showNamespaceScopedOptionSections)
		{
			foreach ((_, var rows) in namespaceOptionSections)
				widthCandidatesNs.AddRange(rows.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		}

		var maxOptWidth = Math.Min(widthCandidatesNs.Max(), 40);
		maxOptWidth = Math.Max(maxOptWidth, "--help, -h".Length);

		var maxChildNsListingW = 0;
		if (node.Children.Count > 0)
			maxChildNsListingW = node.Children.Max(ch => FormatQualifiedCliPath(path, ch.Segment).Length);
		var maxChildCmdListingW = 0;
		if (node.Commands.Count > 0)
			maxChildCmdListingW = node.Commands.Max(c => FormatQualifiedCliPath(path, c.CommandName).Length);

		sb.AppendLine($"\t\tprivate static void PrintHelp_CommandNamespace_{key}()");
		sb.AppendLine("\t\t{");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Usage: \") + CliHelpFormatting.Accent(\"{Escape(entryAssemblyName)}\") + \" {Escape(usagePrefix)} <command> [options]\");");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		if (node.RootCommand is { } nsRootOverview)
			EmitRootCommandHelpOverview(sb, nsRootOverview, "\t\t\t", app, entryAssemblyName);
		else if (!string.IsNullOrWhiteSpace(node.SummaryInnerXml))
		{
			EmitCommandHelpDocPrologue(sb, "\t\t\t", node.SummaryInnerXml, "", false);
			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Global options:\"));");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(\"  \" + CliHelpFormatting.Placeholder(\"{Escape("--help, -h".PadRight(maxOptWidth))}\") + \"  Show help.\");");
		if (showGlobalFlagsInNs)
			EmitHelpOptionRows(sb, globalFlagMembers, maxOptWidth);

		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");

		if (showNamespaceScopedOptionSections)
		{
			foreach ((var segment, var gRows) in namespaceOptionSections)
			{
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"'{Escape(segment)}' options:\"));");
				EmitHelpOptionRows(sb, gRows, maxOptWidth);
				sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
			}
		}

		if (node.Children.Count > 0)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Namespaces:\"));");
			foreach (var ch in node.Children)
			{
				var fullNs = FormatQualifiedCliPath(path, ch.Segment);
				var sumArg = string.IsNullOrWhiteSpace(ch.SummaryOneLiner)
					? "null"
					: $"\"{Escape(ch.SummaryOneLiner)}\"";
				sb.AppendLine(
					$"\t\t\tCliHelpFormatting.WriteHelpListNameAndDescription(true, \"{Escape(fullNs)}\", {sumArg}, {maxChildNsListingW});");
			}

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		if (node.Commands.Count > 0)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Commands:\"));");
			foreach (var c in node.Commands)
			{
				var fullCmd = FormatQualifiedCliPath(path, c.CommandName);
				var sumArg = string.IsNullOrWhiteSpace(c.SummaryOneLiner)
					? "null"
					: $"\"{Escape(c.SummaryOneLiner)}\"";
				sb.AppendLine(
					$"\t\t\tCliHelpFormatting.WriteHelpListNameAndDescription(false, \"{Escape(fullCmd)}\", {sumArg}, {maxChildCmdListingW});");
			}
		}

		if (node.RootCommand is null && !string.IsNullOrWhiteSpace(node.RemarksInnerXml))
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
			EmitNotesSection(sb, "\t\t\t", node.RemarksInnerXml, "");
		}

		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	private static void EmitDispatchForNode(
		StringBuilder sb,
		AppEmitModel app,
		RegistryNode node,
		ImmutableArray<string> path,
		string methodName,
		bool isRoot,
		string entryAssemblyName)
	{
		sb.AppendLine($"\t\tprivate static async Task<int> {methodName}(string[] args, int[] idx, CancellationToken ct)");
		sb.AppendLine("\t\t{");
		// Call namespace options parse for any namespace that has a registered options type (even if it only
		// inherits flags from a parent type) so that flags between the namespace segment and the sub-command
		// are consumed correctly.
		if (!isRoot && node.CommandNamespaceOptionsModel is { FlattenedMembers.Length: > 0 })
			sb.AppendLine($"\t\t\tif (!{CommandNamespaceOptionsParseMethodName(path)}(args, idx)) return 2;");

		sb.AppendLine("\t\t\tif (idx[0] >= args.Length)");
		sb.AppendLine("\t\t\t{");
		if (node.RootCommand is { } dispatchRoot)
			sb.AppendLine($"\t\t\t\treturn await {dispatchRoot.RunMethodName}(TailFrom(args, idx[0]), ct).ConfigureAwait(false);");
		else
		{
			if (isRoot)
				sb.AppendLine("\t\t\t\tPrintRootHelp();");
			else
				sb.AppendLine($"\t\t\t\tPrintHelp_CommandNamespace_{CommandNamespacePathKey(path)}();");

			sb.AppendLine("\t\t\t\treturn 0;");
		}

		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\tif (args[idx[0]] == \"--help\" || args[idx[0]] == \"-h\")");
		sb.AppendLine("\t\t\t{");
		if (isRoot)
			sb.AppendLine("\t\t\t\tPrintRootHelp();");
		else
			sb.AppendLine($"\t\t\t\tPrintHelp_CommandNamespace_{CommandNamespacePathKey(path)}();");

		sb.AppendLine("\t\t\t\treturn 0;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\tvar tok = args[idx[0]];");
		foreach (var cmd in node.Commands)
			EmitOrdinalIgnoreCaseIf(sb, "\t\t\t", "tok", cmd.CommandName, s =>
			{
				s.AppendLine("\t\t\t\tidx[0]++;");
				s.AppendLine($"\t\t\t\treturn await {cmd.RunMethodName}(TailFrom(args, idx[0]), ct).ConfigureAwait(false);");
			});

		foreach (var ch in node.Children)
		{
			var childPath = AppendSegment(path, ch.Segment);
			var childMethod = "DispatchCommandNamespace_" + CommandNamespacePathKey(childPath);
			EmitOrdinalIgnoreCaseIf(sb, "\t\t\t", "tok", ch.Segment, s =>
			{
				s.AppendLine("\t\t\t\tidx[0]++;");
				s.AppendLine($"\t\t\t\treturn await {childMethod}(args, idx, ct).ConfigureAwait(false);");
			});
		}

		sb.AppendLine("\t\t\t{");
		EmitFuzzyDispatchDefault(sb, node, path, entryAssemblyName);
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		foreach (var ch in node.Children)
		{
			var childPath = AppendSegment(path, ch.Segment);
			EmitDispatchForNode(sb, app, ch.Node, childPath, "DispatchCommandNamespace_" + CommandNamespacePathKey(childPath), isRoot: false, entryAssemblyName);
		}
	}

	/// <summary>When <see cref="IMethodSymbol.GetDocumentationCommentXml"/> is empty (e.g. linked sources), recover <c>&lt;summary&gt;</c> from leading doc trivia.</summary>
	private static string TryExtractSummaryDocumentationFragmentFromTrivia(IMethodSymbol method)
	{
		foreach (var sr in method.DeclaringSyntaxReferences)
		{
			if (sr.GetSyntax() is not MethodDeclarationSyntax m)
				continue;
			foreach (var trivia in m.GetLeadingTrivia())
			{
				if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) &&
				    !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
					continue;
				var full = trivia.ToFullString();
				var match = SummaryXmlPattern.Match(full);
				if (!match.Success)
					continue;
				var inner = match.Groups[1].Value;
				inner = DocTriviaStripPattern.Replace(inner, "").Trim();
				if (inner.Length == 0)
					continue;
				return "<summary>" + inner + "</summary>";
			}
		}

		return "";
	}

	private static string TryExtractRemarksDocumentationFragmentFromTrivia(IMethodSymbol method)
	{
		foreach (var sr in method.DeclaringSyntaxReferences)
		{
			if (sr.GetSyntax() is not MethodDeclarationSyntax m)
				continue;
			foreach (var trivia in m.GetLeadingTrivia())
			{
				if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) &&
				    !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
					continue;
				var full = trivia.ToFullString();
				var match = RemarksXmlPattern.Match(full);
				if (!match.Success)
					continue;
				var inner = match.Groups[1].Value;
				inner = DocTriviaStripPattern.Replace(inner, "").Trim();
				if (inner.Length == 0)
					continue;
				return "<remarks>" + inner + "</remarks>";
			}
		}

		return "";
	}

	private static string TryExtractTypeSummaryFromTrivia(INamedTypeSymbol type)
	{
		foreach (var sr in type.DeclaringSyntaxReferences)
		{
			if (sr.GetSyntax() is not BaseTypeDeclarationSyntax typeDecl)
				continue;
			foreach (var trivia in typeDecl.GetLeadingTrivia())
			{
				if (!trivia.HasStructure || trivia.GetStructure() is not DocumentationCommentTriviaSyntax doc)
					continue;
				foreach (var xml in doc.Content)
				{
					if (xml is XmlElementSyntax xe && xe.StartTag.Name.LocalName.ValueText == "summary")
					{
						var s = FlattenXmlSummaryElementText(xe).Trim();
						if (s.Length > 0)
							return s;
					}
				}
			}
		}

		return "";
	}

	private static string FlattenXmlSummaryElementText(XmlElementSyntax xe)
	{
		var sb = new StringBuilder();
		foreach (var n in xe.Content)
		{
			switch (n)
			{
				case XmlTextSyntax txt:
					foreach (var t in txt.TextTokens)
						sb.Append(t.ValueText);
					break;
				case XmlElementSyntax inner:
					sb.Append(FlattenXmlSummaryElementText(inner));
					break;
				case XmlEmptyElementSyntax:
					break;
			}
		}

		return sb.ToString();
	}

	private static string GetTypeListingSummaryOneLiner(INamedTypeSymbol type)
	{
		var fromXml = Documentation.GetTypeSummaryLine(type.GetDocumentationCommentXml());
		if (!string.IsNullOrWhiteSpace(fromXml))
			return fromXml.Trim();

		var trivia = TryExtractTypeSummaryFromTrivia(type);
		return string.IsNullOrWhiteSpace(trivia) ? "" : trivia.Trim();
	}

	private static MethodDocumentation MergeMethodDocumentationFromTrivia(
		IMethodSymbol method,
		MethodDocumentation docs,
		CSharpParseOptions parseOptions)
	{
		if (string.IsNullOrWhiteSpace(docs.SummaryOneLiner))
		{
			var frag = TryExtractSummaryDocumentationFragmentFromTrivia(method);
			if (frag.Length > 0)
			{
				var fromTrivia = Documentation.ParseMethod(frag, parseOptions);
				if (!string.IsNullOrWhiteSpace(fromTrivia.SummaryOneLiner))
				{
					docs = new MethodDocumentation(
						fromTrivia.SummaryOneLiner,
						docs.RemarksRendered,
						docs.ExamplesRendered,
						fromTrivia.SummaryInnerXml,
						docs.RemarksInnerXml,
						docs.ParamDocsRaw,
						docs.ParamSeparators);
				}
			}
		}

		if (string.IsNullOrWhiteSpace(docs.RemarksRendered))
		{
			var rem = TryExtractRemarksDocumentationFragmentFromTrivia(method);
			if (rem.Length > 0)
			{
				var fromTriviaRem = Documentation.ParseMethod(rem, parseOptions);
				if (!string.IsNullOrWhiteSpace(fromTriviaRem.RemarksRendered))
				{
					docs = new MethodDocumentation(
						docs.SummaryOneLiner,
						fromTriviaRem.RemarksRendered,
						docs.ExamplesRendered,
						docs.SummaryInnerXml,
						fromTriviaRem.RemarksInnerXml,
						docs.ParamDocsRaw,
						docs.ParamSeparators);
				}
			}
		}

		return docs;
	}

	private static string GetCommandRoutePath(CommandModel cmd)
	{
		if (cmd.IsRootDefault)
		{
			if (cmd.RoutePrefix.IsDefaultOrEmpty)
				return "<root>";
			return string.Join("/", cmd.RoutePrefix) + "/<root>";
		}

		if (cmd.RoutePrefix.IsDefaultOrEmpty)
			return cmd.CommandName;
		return string.Join("/", cmd.RoutePrefix) + "/" + cmd.CommandName;
	}

	/// <summary>Help printer invoked from generated command runners for default/root handlers (overview lives in root or namespace overview).</summary>
	private static string HelpPrinterMethodForCommand(CommandModel cmd)
	{
		if (cmd.IsRootDefault)
		{
			if (cmd.RoutePrefix.IsDefaultOrEmpty)
				return "PrintRootHelp";
			return "PrintHelp_CommandNamespace_" + CommandNamespacePathKey(cmd.RoutePrefix);
		}

		return "PrintHelp_" + cmd.RunMethodName;
	}

	/// <summary>Indented body lines for default-handler summary/remarks (one indent step less than before).</summary>
	private static void EmitRootDefaultDocumentationLines(StringBuilder sb, string indent, string? innerXml, string? plainFallback, bool isRemarks)
	{
		if (!string.IsNullOrWhiteSpace(innerXml))
		{
			// Concatenate (do not use $"..." interpolation): inner XML can contain `{`/`}` from generic cref text.
			sb.AppendLine(indent + "global::Nullean.Argh.Help.XmlDocumentationRenderer.WriteIndentedDoc(Console.Out, \"   \", \"" + EscapeDocXml(innerXml!) + "\", " + (isRemarks ? "true" : "false") + ");");
			return;
		}

		if (string.IsNullOrWhiteSpace(plainFallback))
			return;
		var text = plainFallback!;
		foreach (var part in text.Replace("\r\n", "\n").Split('\n'))
		{
			var line = part.TrimEnd('\r');
			if (string.IsNullOrWhiteSpace(line))
				sb.AppendLine($"{indent}Console.Out.WriteLine();");
			else
				sb.AppendLine($"{indent}Console.Out.WriteLine(\"   \" + \"{Escape(line.Trim())}\");");
		}
	}

	/// <summary>Summary (white) after usage, or remarks (gray) after options; caller emits <c>Notes:</c> before remarks when using per-command help.</summary>
	private static void EmitCommandHelpDocPrologue(StringBuilder sb, string indent, string? innerXml, string? plainFallback, bool remarks)
	{
		if (!string.IsNullOrWhiteSpace(innerXml))
		{
			sb.AppendLine(indent + "global::Nullean.Argh.Help.XmlDocumentationRenderer.WriteIndentedDoc(Console.Out, \"   \", \"" + EscapeDocXml(innerXml!) + "\", " + (remarks ? "true" : "false") + ");");
			return;
		}

		if (string.IsNullOrWhiteSpace(plainFallback))
			return;
		var text = plainFallback!;
		var styler = remarks ? "CliHelpFormatting.DocRemarksLine" : "CliHelpFormatting.DocSummaryLine";
		foreach (var part in text.Replace("\r\n", "\n").Split('\n'))
		{
			var line = part.TrimEnd('\r');
			if (string.IsNullOrWhiteSpace(line))
				sb.AppendLine($"{indent}Console.Out.WriteLine();");
			else
				sb.AppendLine($"{indent}Console.Out.WriteLine(\"   \" + {styler}(\"{Escape(line.Trim())}\"));");
		}
	}

	/// <summary>Flatten remarks inner XML to plain text at generation time (used for single-line detection).</summary>
	private static string FlattenRemarksXml(string? innerXml)
	{
		if (string.IsNullOrWhiteSpace(innerXml))
			return "";
		try
		{
			var el = XElement.Parse("<x>" + innerXml + "</x>", LoadOptions.PreserveWhitespace);
			return Documentation.FlattenBlockPublic(el).Replace("\r\n", "\n").Trim();
		}
		catch { return ""; }
	}

	/// <summary>
	/// Emits the Notes: section for command/namespace help.
	/// Single-line remarks are inlined on the same line as "Notes:";
	/// multi-line remarks follow on the next line with 2-space indent to align with Commands/Options above.
	/// </summary>
	private static void EmitNotesSection(StringBuilder sb, string indent, string? innerXml, string plainRendered)
	{
		var flat = string.IsNullOrWhiteSpace(plainRendered)
			? FlattenRemarksXml(innerXml)
			: plainRendered.Replace("\r\n", "\n").Trim();
		if (string.IsNullOrWhiteSpace(flat))
			return;

		var singleLine = !flat.Contains('\n');
		if (singleLine)
		{
			sb.AppendLine($"{indent}Console.Out.WriteLine(CliHelpFormatting.Section(\"Notes:\") + \"  \" + CliHelpFormatting.DocRemarksLine(\"{Escape(flat)}\"));");
			return;
		}

		sb.AppendLine($"{indent}Console.Out.WriteLine(CliHelpFormatting.Section(\"Notes:\"));");
		if (!string.IsNullOrWhiteSpace(innerXml))
		{
			sb.AppendLine(indent + "global::Nullean.Argh.Help.XmlDocumentationRenderer.WriteIndentedDoc(Console.Out, \"  \", \"" + EscapeDocXml(innerXml!) + "\", true);");
			return;
		}
		foreach (var part in flat.Split('\n'))
		{
			var line = part.TrimEnd('\r');
			if (string.IsNullOrWhiteSpace(line))
				sb.AppendLine($"{indent}Console.Out.WriteLine();");
			else
				sb.AppendLine($"{indent}Console.Out.WriteLine(\"  \" + CliHelpFormatting.DocRemarksLine(\"{Escape(line.Trim())}\"));");
		}
	}

	private static void EmitRootCommandHelpOverview(StringBuilder sb, CommandModel rootCmd, string indent, AppEmitModel app, string entryAssemblyName)
	{
		// "(default command)" is not an argv token — labels the opt-in default handler; summary/remarks from XML on the handler.
		sb.AppendLine($"{indent}Console.Out.WriteLine(\" \" + CliHelpFormatting.DefaultCommandLabel(\"(default command)\"));");
		EmitRootDefaultDocumentationLines(sb, indent, rootCmd.SummaryInnerXml, rootCmd.SummaryOneLiner, false);
		var remarksXml = TransformRemarksInnerXmlForHelp(rootCmd.RemarksInnerXml, rootCmd, app.AllCommands, entryAssemblyName);
		EmitRootDefaultDocumentationLines(sb, indent, remarksXml, rootCmd.RemarksRendered, true);
		var rootFlags = rootCmd.Parameters.Where(static p => p.Kind == ParameterKind.Flag).ToList();
		if (rootFlags.Count > 0)
		{
			var mw = Math.Min(
				Math.Max(rootFlags.Max(p => HelpLayout.FormatOptionLeftCell(p).Length), "--help, -h".Length),
				40);
			sb.AppendLine($"{indent}Console.Out.WriteLine();");
			sb.AppendLine($"{indent}Console.Out.WriteLine(CliHelpFormatting.Section(\"Options for this default:\"));");
			foreach (var p in rootFlags)
			{
				var left = HelpLayout.FormatOptionLeftCell(p).PadRight(mw);
				var desc = BuildDescriptionSuffix(p, forPositional: false);
				sb.AppendLine(
					$"{indent}Console.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {Escape(desc)}\");");
			}
		}

		sb.AppendLine($"{indent}Console.Out.WriteLine();");
	}

	/// <summary>Space-separated CLI path for help listings (e.g. <c>storage blob upload</c>).</summary>
	private static string FormatQualifiedCliPath(ImmutableArray<string> prefix, string segment)
	{
		if (prefix.IsDefaultOrEmpty)
			return segment;
		return string.Join(" ", prefix) + " " + segment;
	}

	private static void EmitArghGeneratedRouteArgsMethod(StringBuilder sb)
	{
		sb.AppendLine("\t\tpublic static RouteMatch? Route(string[] args)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tif (args is null)");
		sb.AppendLine("\t\t\t\tthrow new ArgumentNullException(nameof(args));");
		sb.AppendLine("\t\t\tif (!TryParseRoute(args, out var m))");
		sb.AppendLine("\t\t\t\treturn null;");
		sb.AppendLine("\t\t\treturn m;");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}


	private static void EmitTryParseRouteHierarchical(StringBuilder sb, AppEmitModel app)
	{
		var hasGlobal = app.GlobalOptionsModel is { Members: { Length: > 0 } };
		sb.AppendLine("\t\tpublic static bool TryParseRoute(string[] args, out RouteMatch match)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tmatch = default;");
		sb.AppendLine("\t\t\tif (CompletionProtocol.IsArghMetaCompletionInvocation(args)) return false;");
		sb.AppendLine("\t\t\tvar idx = new int[1];");
		if (hasGlobal)
			sb.AppendLine("\t\t\tif (!TryParseGlobalOptions(args, idx)) return false;");
		sb.AppendLine("\t\t\tif (args.Length == 0) return false;");
		sb.AppendLine("\t\t\tif (idx[0] < args.Length && (args[idx[0]] == \"--help\" || args[idx[0]] == \"-h\")) return false;");
		sb.AppendLine("\t\t\tif (idx[0] < args.Length && args[idx[0]] == \"--version\") return false;");
		sb.AppendLine("\t\t\tif (idx[0] >= args.Length)");
		sb.AppendLine("\t\t\t{");
		if (app.Root.RootCommand is { } routeRoot)
		{
			var rp = Escape(GetCommandRoutePath(routeRoot));
			sb.AppendLine($"\t\t\t\tmatch = new RouteMatch(\"{rp}\", TailFrom(args, idx[0]));");
			sb.AppendLine("\t\t\t\treturn true;");
		}
		else
		{
			sb.AppendLine("\t\t\t\treturn false;");
		}

		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\treturn TryParseRouteRoot(args, idx, out match);");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		EmitTryParseRouteForNode(sb, app, app.Root, ImmutableArray<string>.Empty, "TryParseRouteRoot", isRoot: true);
		EmitArghGeneratedRouteArgsMethod(sb);
	}

	private static void EmitTryParseRouteForNode(
		StringBuilder sb,
		AppEmitModel app,
		RegistryNode node,
		ImmutableArray<string> path,
		string methodName,
		bool isRoot)
	{
		sb.AppendLine($"\t\tprivate static bool {methodName}(string[] args, int[] idx, out RouteMatch match)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tmatch = default;");
		if (!isRoot && node.CommandNamespaceOptionsModel is { FlattenedMembers.Length: > 0 })
			sb.AppendLine($"\t\t\tif (!{CommandNamespaceOptionsParseMethodName(path)}(args, idx)) return false;");
		sb.AppendLine("\t\t\tif (idx[0] >= args.Length)");
		sb.AppendLine("\t\t\t{");
		if (node.RootCommand is { } routeNsRoot)
		{
			var rnp = Escape(GetCommandRoutePath(routeNsRoot));
			sb.AppendLine($"\t\t\t\tmatch = new RouteMatch(\"{rnp}\", TailFrom(args, idx[0]));");
			sb.AppendLine("\t\t\t\treturn true;");
		}
		else
		{
			sb.AppendLine("\t\t\t\treturn false;");
		}

		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\tif (args[idx[0]] == \"--help\" || args[idx[0]] == \"-h\") return false;");
		sb.AppendLine("\t\t\tvar tokKey = args[idx[0]].ToLowerInvariant();");
		sb.AppendLine("\t\t\tswitch (tokKey)");
		sb.AppendLine("\t\t\t{");
		foreach (var cmd in node.Commands)
		{
			var routePath = Escape(GetCommandRoutePath(cmd));
			var caseLabel = Escape(cmd.CommandName.ToLowerInvariant());
			sb.AppendLine($"\t\t\t\tcase \"{caseLabel}\":");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\tidx[0]++;");
			sb.AppendLine($"\t\t\t\t\tmatch = new RouteMatch(\"{routePath}\", TailFrom(args, idx[0]));");
			sb.AppendLine("\t\t\t\t\treturn true;");
			sb.AppendLine("\t\t\t\t}");
		}

		foreach (var ch in node.Children)
		{
			var childPath = AppendSegment(path, ch.Segment);
			var childMethod = "TryParseRouteCommandNamespace_" + CommandNamespacePathKey(childPath);
			var caseLabel = Escape(ch.Segment.ToLowerInvariant());
			sb.AppendLine($"\t\t\t\tcase \"{caseLabel}\":");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\tidx[0]++;");
			sb.AppendLine($"\t\t\t\t\treturn {childMethod}(args, idx, out match);");
			sb.AppendLine("\t\t\t\t}");
		}

		sb.AppendLine("\t\t\t\tdefault:");
		sb.AppendLine("\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		foreach (var ch in node.Children)
		{
			var childPath = AppendSegment(path, ch.Segment);
			EmitTryParseRouteForNode(sb, app, ch.Node, childPath, "TryParseRouteCommandNamespace_" + CommandNamespacePathKey(childPath), isRoot: false);
		}
	}

	private static CommandModel SyntheticOptionsCommand(ImmutableArray<ParameterModel> members, string runMethodName) =>
		new(
			ImmutableArray<string>.Empty,
			"__opt__",
			runMethodName,
			"object",
			"__noop",
			false,
			false,
			"global::System.Void",
			false,
			true,
			members,
			false,
			ImmutableArray<HandlerParam>.Empty,
			SourceSpanInfo.None,
			ImmutableArray<(string, string)>.Empty,
			"",   // HandlerDocCommentId
			"",   // SummaryOneLiner
			"",   // RemarksRendered
			"",   // SummaryInnerXml
			"",   // RemarksInnerXml
			"",   // ExamplesRendered
			"",   // UsageHints
			ImmutableArray<(string, bool)>.Empty);

	private static void EmitAllowedFlagPredicate(StringBuilder sb, ImmutableArray<ParameterModel> members)
	{
		sb.AppendLine("\t\t\tbool IsAllowedFlag(string name) => name switch");
		sb.AppendLine("\t\t\t{");
		foreach (var p in members)
		{
			if (p.Kind != ParameterKind.Flag)
				continue;
			sb.AppendLine($"\t\t\t\t\"{Escape(p.CliLongName)}\" => true,");
			foreach (var al in p.Aliases)
			{
				if (string.Equals(al, p.CliLongName, StringComparison.OrdinalIgnoreCase))
					continue;
				sb.AppendLine($"\t\t\t\t\"{Escape(al)}\" => true,");
			}

			if (p.Special == BoolSpecialKind.NullableBool)
				sb.AppendLine($"\t\t\t\t\"no-{Escape(p.CliLongName)}\" => true,");
		}

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");
	}

	private static void EmitOptionsTryParse(
		StringBuilder sb,
		string methodName,
		ImmutableArray<ParameterModel> members,
		string? storeTypeFq = null,
		string? storeFieldName = null,
		ImmutableArray<string>? storeBestCtorParamOrder = null)
	{
		var syn = SyntheticOptionsCommand(members, methodName);
		sb.AppendLine($"\t\tprivate static bool {methodName}(string[] args, int[] idx)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tvar flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);");
		EmitBoolSwitchNames(sb, syn);
		EmitCanonFlagNameMethod(sb, syn);
		EmitShortFlagMethods(sb, syn);
		EmitAllowedFlagPredicate(sb, members);
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
		sb.AppendLine("\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'.\");");
		sb.AppendLine("\t\t\t\t\t\t\treturn false;");
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
		sb.AppendLine("\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown option '--{flagName}'.\");");
		sb.AppendLine("\t\t\t\t\t\t\treturn false;");
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
		sb.AppendLine("\t\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t\t\tidx[0]++;");
		sb.AppendLine("\t\t\t\t\t\tcontinue;");
		sb.AppendLine("\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\tif (a.Length == 2)");
		sb.AppendLine("\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\tvar sc = a[1];");
		sb.AppendLine("\t\t\t\t\t\tif (IsShortBoolChar(sc))");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tif (!TryApplyShortFlag(sc, \"true\"))");
		sb.AppendLine("\t\t\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t\t\t\tidx[0]++;");
		sb.AppendLine("\t\t\t\t\t\t\tcontinue;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\tif (idx[0] + 1 >= args.Length)");
		sb.AppendLine("\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: missing value for short flag '-{sc}'.\");");
		sb.AppendLine("\t\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t\t\t}");
		sb.AppendLine("\t\t\t\t\t\tif (!TryApplyShortFlag(sc, args[idx[0] + 1]))");
		sb.AppendLine("\t\t\t\t\t\t\treturn false;");
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
				sb.AppendLine($"\t\t\t{GetCSharpCliType(m)} {m.LocalVarName} = {GetCliInitializer(m)};");
				sb.AppendLine($"\t\t\tflags.TryGetValue(\"{Escape(m.CliLongName)}\", out var {m.LocalVarName}Text);");
				EmitParseAndAssign(sb, m, m.LocalVarName + "Text", m.LocalVarName, "return false", null);
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
							"long" => $"long.TryParse({tmpName}Txt, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"double" => $"double.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"float" => $"float.TryParse({tmpName}Txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var {tmpName}P) ? {tmpName}P : {tmpName}",
							"string" or "string?" => $"{tmpName}Txt ?? {tmpName}",
							_ => $"{tmpName}Txt != null ? {tmpName}Txt : {tmpName}"
						};
						if (m.TypeName is "string" or "string?")
							sb.AppendLine($"\t\t\t{tmpName} = {parseExpr};");
						else
							sb.AppendLine($"\t\t\tif ({tmpName}Txt != null) {tmpName} = {parseExpr};");
					}
					// Enum and custom parsers: keep static fallback (complex re-parse not emitted here for brevity)
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

	private static void EmitBindCollectionParameter(StringBuilder sb, ParameterModel p, bool multiFlagsAvailable, string failureExit = "return 2", string? helpMethodName = null)
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
				if (helpMethodName is not null)
					sb.AppendLine($"\t\t\t\t{helpMethodName}();");
				sb.AppendLine($"\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t}");
			}
			else
			{
				sb.AppendLine($"\t\t\t\t{p.LocalVarName}Joined = null;");
				sb.AppendLine("\t\t\t}");
			}

			sb.AppendLine($"\t\t\tif (!string.IsNullOrEmpty({p.LocalVarName}Joined))");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine($"\t\t\t\tvar __sep_{p.LocalVarName} = \"{Escape(sep)}\";");
			sb.AppendLine($"\t\t\t\tforeach (var __part in {p.LocalVarName}Joined.Split(__sep_{p.LocalVarName}, StringSplitOptions.None))");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\tif (string.IsNullOrEmpty(__part)) continue;");
			EmitParseFromString(sb, elemModel, "__part", "__ce_" + p.LocalVarName, indentExtra: "\t\t", outVarKeyword: true, failureExit: failureExit, helpMethodName: helpMethodName);
			sb.AppendLine($"\t\t\t\t\t{acc}.Add(__ce_{p.LocalVarName});");
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
			EmitParseFromString(sb, elemModel, "__raw", "__ce_" + p.LocalVarName, indentExtra: "\t", outVarKeyword: true, failureExit: failureExit, helpMethodName: helpMethodName);
			sb.AppendLine($"\t\t\t\t{acc}.Add(__ce_{p.LocalVarName});");
			sb.AppendLine("\t\t\t}");
			if (p.IsRequired)
			{
				sb.AppendLine($"\t\t\tif ({acc}.Count == 0)");
				sb.AppendLine("\t\t\t{");
				sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine($\"Error: missing required flag --{flagKey}.\");");
				if (helpMethodName is not null)
					sb.AppendLine($"\t\t\t\t{helpMethodName}();");
				sb.AppendLine($"\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t}");
			}
		}

		var declType = p.FullDeclaredTypeFq ?? "object";
		if (p.CollectionTargetIsArray)
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
				sb.Append(ctor[i].LocalVarName);
			}

			sb.Append(")");
			if (init.Length > 0)
			{
				sb.AppendLine();
				sb.AppendLine("\t\t\t{");
				foreach (var ip in init)
					sb.AppendLine($"\t\t\t\t{ip.AsParametersClrName} = {ip.LocalVarName},");
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
			sb.Append(ctor[i].LocalVarName);
		}

		sb.Append(")");
		if (init.Length > 0)
		{
			sb.AppendLine();
			sb.AppendLine("\t\t\t{");
			foreach (var ip in init)
				sb.AppendLine($"\t\t\t\t{ip.AsParametersClrName} = {ip.LocalVarName},");
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

	private static void EmitCommandRunner(
		StringBuilder sb,
		CommandModel cmd,
		ImmutableArray<GlobalMiddlewareRegistration> globalMiddleware,
		bool emitDtoTryParse = false,
		string? dtoMethodName = null,
		string? dtoResultTypeFq = null,
		string? dtoOptionsTypeFq = null,
		ImmutableArray<string>? dtoOptionsBestCtorParamOrder = null,
		ImmutableArray<(string TypeFq, string TypeMetadataName, ImmutableArray<string> AllBaseTypeMetadataNames, string StaticFieldName, string LocalVarName, ImmutableArray<ParameterModel> FlatMembers, ImmutableArray<string>? BestCtorParamOrder)> injectedOptions = default)
	{
		var anyRepeatedCollection = cmd.Parameters.Any(static p =>
			p is { IsCollection: true, Kind: ParameterKind.Flag } && p.CollectionSeparator is null);

		var failureExit = emitDtoTryParse ? "return false" : "return 2";
		var helpMethodName = emitDtoTryParse ? null : HelpPrinterMethodForCommand(cmd);

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

		EmitCliValueDeclarations(sb, cmd);

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
		EmitShortFlagMethods(sb, cmd);
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
		sb.AppendLine("\t\t\t\t\t\t\tif (i + 1 >= args.Length)");
		sb.AppendLine("\t\t\t\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: missing value for flag --{flagName}.\");");
		if (helpMethodName is not null)
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
		sb.AppendLine("\t\t\t\t\t\tif (!TryApplyShortFlag(shortKey[0], a.Substring(eqs + 1)))");
		sb.AppendLine($"\t\t\t\t\t\t\t{failureExit};");
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
				EmitBindCollectionParameter(sb, p, anyRepeatedCollection, failureExit, helpMethodName);
				continue;
			}

			var flagKey = Escape(p.CliLongName);
			sb.AppendLine($"\t\t\tif (!flags.TryGetValue(\"{flagKey}\", out var {p.LocalVarName}Text))");
			if (p.IsRequired)
			{
				sb.AppendLine("\t\t\t{");
				sb.AppendLine($"\t\t\t\tConsole.Error.WriteLine($\"Error: missing required flag --{flagKey}.\");");
				if (helpMethodName is not null)
					sb.AppendLine($"\t\t\t\t{helpMethodName}();");
				sb.AppendLine($"\t\t\t\t{failureExit};");
				sb.AppendLine("\t\t\t}");
			}
			else
				sb.AppendLine($"\t\t\t\t{p.LocalVarName}Text = null;");

			EmitParseAndAssign(sb, p, p.LocalVarName + "Text", p.LocalVarName, failureExit, helpMethodName);
		}

		var posIndex = 0;
		foreach (var p in cmd.Parameters)
		{
			if (p.Kind != ParameterKind.Positional)
				continue;

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
				EmitParseFromString(sb, p, $"positionals[{posIndex}]", p.LocalVarName, indentExtra: "\t", failureExit: failureExit, helpMethodName: helpMethodName);
				sb.AppendLine("\t\t\t}");
			}
			else
			{
				var fallback = p.DefaultValueLiteral ?? "default!";
				sb.AppendLine($"\t\t\tif (positionals.Count <= {posIndex})");
				sb.AppendLine($"\t\t\t\t{p.LocalVarName} = {fallback};");
				sb.AppendLine("\t\t\telse");
				sb.AppendLine("\t\t\t{");
				EmitParseFromString(sb, p, $"positionals[{posIndex}]", p.LocalVarName, indentExtra: "\t", failureExit: failureExit, helpMethodName: helpMethodName);
				sb.AppendLine("\t\t\t}");
			}

			posIndex++;
		}

		if (emitDtoTryParse)
		{
			if (dtoOptionsTypeFq is not null)
				EmitOptionsDtoConstructionAndReturn(sb, dtoOptionsTypeFq, cmd.Parameters, dtoOptionsBestCtorParamOrder);
			else
				EmitAsParametersConstructionForDto(sb, cmd);

			sb.AppendLine("\t\t}");
			sb.AppendLine();
			return;
		}

		EmitAsParametersConstruction(sb, cmd);

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

	private static void EmitCliValueDeclarations(StringBuilder sb, CommandModel cmd)
	{
		foreach (var p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Injected || p.Kind == ParameterKind.OptionsInjected)
				continue;

			if (p.Special == BoolSpecialKind.Bool || p.Special == BoolSpecialKind.NullableBool)
				continue;

			if (p.IsCollection && p.Kind == ParameterKind.Flag)
			{
				var elemFq = GetElementCSharpFq(p);
				sb.AppendLine(
					$"\t\t\tvar {p.LocalVarName}_acc = new global::System.Collections.Generic.List<{elemFq}>();");
				continue;
			}

			sb.AppendLine($"\t\t\t{GetCSharpCliType(p)} {p.LocalVarName} = {GetCliInitializer(p)};");
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
			ParserTypeFq = p.ElementParserTypeFq,
			CustomValueTypeFq = p.ElementCustomValueTypeFq,
			Special = BoolSpecialKind.None,
			IsCollection = false,
			IsRequired = true
		};

	private static string GetCSharpCliType(ParameterModel p)
	{
		if (p.ScalarKind == CliScalarKind.Collection && p.FullDeclaredTypeFq is not null)
			return p.FullDeclaredTypeFq;

		switch (p.ScalarKind)
		{
			case CliScalarKind.Enum when p.EnumTypeFq is not null:
				return p.IsRequired ? p.EnumTypeFq : p.EnumTypeFq + "?";
			case CliScalarKind.FileInfo:
				return p.IsRequired ? "global::System.IO.FileInfo" : "global::System.IO.FileInfo?";
			case CliScalarKind.DirectoryInfo:
				return p.IsRequired ? "global::System.IO.DirectoryInfo" : "global::System.IO.DirectoryInfo?";
			case CliScalarKind.Uri:
				return p.IsRequired ? "global::System.Uri" : "global::System.Uri?";
			case CliScalarKind.CustomParser when p.CustomValueTypeFq is not null:
				return p.IsRequired ? p.CustomValueTypeFq : p.CustomValueTypeFq + "?";
			default:
				break;
		}

		if (p.TypeName == "string")
			return p.IsRequired ? "string" : "string?";

		return p.TypeName switch
		{
			"int" => "int",
			"long" => "long",
			"float" => "float",
			"double" => "double",
			"decimal" => "decimal",
			"bool" => "bool",
			"bool?" => "bool?",
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

	private static void EmitBoolSwitchNames(StringBuilder sb, CommandModel cmd, bool suppressNoNameHelper = false)
	{
		var names = new List<string>();
		var noNames = new List<string>();
		foreach (var p in cmd.Parameters)
		{
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

		sb.AppendLine("\t\t\tbool IsBoolSwitchName(string name) => name switch");
		sb.AppendLine("\t\t\t{");
		foreach (var n in names)
			sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");
		foreach (var n in noNames)
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
			sb.AppendLine("\t\t\tbool IsBoolSwitchNoName(string name) => name switch");
			sb.AppendLine("\t\t\t{");
			foreach (var n in noNames)
				sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");
			sb.AppendLine("\t\t\t\t_ => false");
			sb.AppendLine("\t\t\t};");
		}
	}

	private static void EmitCanonFlagNameMethod(StringBuilder sb, CommandModel cmd)
	{
		var cases = new List<(string from, string to)>();
		foreach (var p in cmd.Parameters)
		{
			if (p.Kind != ParameterKind.Flag)
				continue;
			foreach (var al in p.Aliases)
			{
				if (string.Equals(al, p.CliLongName, StringComparison.OrdinalIgnoreCase))
					continue;
				cases.Add((al, p.CliLongName));
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

	private static void EmitShortFlagMethods(StringBuilder sb, CommandModel cmd)
	{
		var shortCases = new List<(char c, string Primary, bool IsBool)>();
		foreach (var p in cmd.Parameters)
		{
			if (p.Kind != ParameterKind.Flag)
				continue;
			if (p.ShortOpt is not char ch)
				continue;
			shortCases.Add((ch, p.CliLongName, p.Special == BoolSpecialKind.Bool));
		}

		if (shortCases.Count == 0)
		{
			sb.AppendLine("\t\t\tbool TryApplyShortFlag(char c, string val)");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tConsole.Error.WriteLine($\"Error: unknown short option '-{c}'.\");");
			sb.AppendLine("\t\t\t\treturn false;");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine("\t\t\tbool IsShortBoolChar(char c) => false;");
			return;
		}

		sb.AppendLine("\t\t\tbool TryApplyShortFlag(char c, string val)");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tswitch (c)");
		sb.AppendLine("\t\t\t\t{");
		foreach ((var c, var primary, _) in shortCases)
		{
			var esc = Escape(primary);
			sb.AppendLine($"\t\t\t\t\tcase '{c}':");
			sb.AppendLine($"\t\t\t\t\t\tflags[\"{esc}\"] = val;");
			sb.AppendLine("\t\t\t\t\t\treturn true;");
		}

		sb.AppendLine("\t\t\t\t\tdefault:");
		sb.AppendLine("\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown short option '-{c}'.\");");
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
		foreach ((var c, _, var isBool) in shortCases)
		{
			if (isBool)
				sb.AppendLine($"\t\t\t\t'{c}' => true,");
		}

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");
	}

	private static void EmitParseAndAssign(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string failureExit = "return 2", string? helpMethodName = null)
	{
		if (!p.IsRequired && p.DefaultValueLiteral is not null)
		{
			sb.AppendLine($"\t\t\tif ({rawExpr} is null)");
			sb.AppendLine($"\t\t\t\t{targetVar} = {p.DefaultValueLiteral};");
			sb.AppendLine("\t\t\telse");
			sb.AppendLine("\t\t\t{");
			EmitParseFromString(sb, p, rawExpr, targetVar, indentExtra: "\t", outVarKeyword: false, failureExit: failureExit, helpMethodName: helpMethodName);
			sb.AppendLine("\t\t\t}");
		}
		else
			EmitParseFromString(sb, p, rawExpr, targetVar, failureExit: failureExit, helpMethodName: helpMethodName);
	}

	private static void EmitParseFromString(StringBuilder sb, ParameterModel p, string rawExpr, string targetVar, string indentExtra = "",
		bool outVarKeyword = false, string failureExit = "return 2", string? helpMethodName = null)
	{
		var ind = "\t\t\t" + indentExtra;
		var e = Escape(p.CliLongName);
		string Out(string name) => outVarKeyword ? "out var " + name : "out " + name;

		if (p.ScalarKind == CliScalarKind.Enum && p.EnumTypeFq is not null)
		{
			sb.AppendLine($"{ind}if (!global::System.Enum.TryParse<{p.EnumTypeFq}>({rawExpr}, true, out var __ev) || !global::System.Enum.IsDefined(typeof({p.EnumTypeFq}), __ev))");
			sb.AppendLine($"{ind}{{");
			sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid value for --{e}: '{{{rawExpr}}}'.\");");
			if (helpMethodName is not null) sb.AppendLine($"{ind}\t{helpMethodName}();");
			sb.AppendLine($"{ind}\t{failureExit};");
			sb.AppendLine($"{ind}}}");
			if (outVarKeyword)
				sb.AppendLine($"{ind}var {targetVar} = __ev;");
			else
				sb.AppendLine($"{ind}{targetVar} = __ev;");
			return;
		}

		if (p.ScalarKind == CliScalarKind.FileInfo)
		{
			if (outVarKeyword)
				sb.AppendLine($"{ind}var {targetVar} = new global::System.IO.FileInfo({rawExpr}!);");
			else
				sb.AppendLine($"{ind}{targetVar} = new global::System.IO.FileInfo({rawExpr}!);");
			return;
		}

		if (p.ScalarKind == CliScalarKind.DirectoryInfo)
		{
			if (outVarKeyword)
				sb.AppendLine($"{ind}var {targetVar} = new global::System.IO.DirectoryInfo({rawExpr}!);");
			else
				sb.AppendLine($"{ind}{targetVar} = new global::System.IO.DirectoryInfo({rawExpr}!);");
			return;
		}

		if (p.ScalarKind == CliScalarKind.Uri)
		{
			sb.AppendLine($"{ind}if (!global::System.Uri.TryCreate({rawExpr}, global::System.UriKind.RelativeOrAbsolute, out var __uri))");
			sb.AppendLine($"{ind}{{");
			sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid URI for --{e}: '{{{rawExpr}}}'.\");");
			if (helpMethodName is not null) sb.AppendLine($"{ind}\t{helpMethodName}();");
			sb.AppendLine($"{ind}\t{failureExit};");
			sb.AppendLine($"{ind}}}");
			if (outVarKeyword)
				sb.AppendLine($"{ind}var {targetVar} = __uri;");
			else
				sb.AppendLine($"{ind}{targetVar} = __uri;");
			return;
		}

		if (p.ScalarKind == CliScalarKind.CustomParser && p.ParserTypeFq is not null && p.CustomValueTypeFq is not null)
		{
			sb.AppendLine($"{ind}var __parser = new {p.ParserTypeFq}();");
			sb.AppendLine($"{ind}if (!__parser.TryParse({rawExpr}!, out var __pv))");
			sb.AppendLine($"{ind}{{");
			sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid value for --{e}.\");");
			if (helpMethodName is not null) sb.AppendLine($"{ind}\t{helpMethodName}();");
			sb.AppendLine($"{ind}\t{failureExit};");
			sb.AppendLine($"{ind}}}");
			if (outVarKeyword)
				sb.AppendLine($"{ind}var {targetVar} = __pv;");
			else
				sb.AppendLine($"{ind}{targetVar} = __pv;");
			return;
		}

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
				if (helpMethodName is not null) sb.AppendLine($"{ind}\t{helpMethodName}();");
				sb.AppendLine($"{ind}\t{failureExit};");
				sb.AppendLine($"{ind}}}");
				break;
			case BoolSpecialKind.None when p.TypeName == "long":
				sb.AppendLine(
					$"{ind}if (!long.TryParse({rawExpr}, NumberStyles.Integer, CultureInfo.InvariantCulture, {Out(targetVar)}))");
				sb.AppendLine($"{ind}{{");
				sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid long for --{e}: '{{{rawExpr}}}'.\");");
				if (helpMethodName is not null) sb.AppendLine($"{ind}\t{helpMethodName}();");
				sb.AppendLine($"{ind}\t{failureExit};");
				sb.AppendLine($"{ind}}}");
				break;
			case BoolSpecialKind.None when p.TypeName == "float":
				sb.AppendLine(
					$"{ind}if (!float.TryParse({rawExpr}, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, {Out(targetVar)}))");
				sb.AppendLine($"{ind}{{");
				sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid float for --{e}: '{{{rawExpr}}}'.\");");
				if (helpMethodName is not null) sb.AppendLine($"{ind}\t{helpMethodName}();");
				sb.AppendLine($"{ind}\t{failureExit};");
				sb.AppendLine($"{ind}}}");
				break;
			case BoolSpecialKind.None when p.TypeName == "double":
				sb.AppendLine(
					$"{ind}if (!double.TryParse({rawExpr}, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, {Out(targetVar)}))");
				sb.AppendLine($"{ind}{{");
				sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid double for --{e}: '{{{rawExpr}}}'.\");");
				if (helpMethodName is not null) sb.AppendLine($"{ind}\t{helpMethodName}();");
				sb.AppendLine($"{ind}\t{failureExit};");
				sb.AppendLine($"{ind}}}");
				break;
			case BoolSpecialKind.None when p.TypeName == "decimal":
				sb.AppendLine(
					$"{ind}if (!decimal.TryParse({rawExpr}, NumberStyles.Number, CultureInfo.InvariantCulture, {Out(targetVar)}))");
				sb.AppendLine($"{ind}{{");
				sb.AppendLine($"{ind}\tConsole.Error.WriteLine($\"Error: invalid decimal for --{e}: '{{{rawExpr}}}'.\");");
				if (helpMethodName is not null) sb.AppendLine($"{ind}\t{helpMethodName}();");
				sb.AppendLine($"{ind}\t{failureExit};");
				sb.AppendLine($"{ind}}}");
				break;
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
				else
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
		// Empty string means no return type info (shouldn't happen for method handlers)
		if (retFq == "" || retFq == "global::System.Void")
		{
			sb.AppendLine($"{lineIndent}{call};");
			sb.AppendLine(ret0);
			return;
		}

		if (retFq == "global::System.Int32")
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
			else if (lambdaRetFq == "global::System.Int32")
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

	private static void EmitHelpOptionRows(StringBuilder sb, IReadOnlyList<ParameterModel> rows, int maxOptWidth)
	{
		foreach (var p in rows)
		{
			var left = HelpLayout.FormatOptionLeftCell(p).PadRight(maxOptWidth);
			var desc = BuildDescriptionSuffix(p, forPositional: false);
			sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {Escape(desc)}\");");
		}
	}

	private static void EmitCommandHelpPrinter(StringBuilder sb, CommandModel cmd, AppEmitModel app, string entryAssemblyName)
	{
		if (cmd.IsRootDefault)
			return;

		var routeUsage = cmd.RoutePrefix.IsDefaultOrEmpty
			? ""
			: string.Join(" ", cmd.RoutePrefix) + " ";

		var globalFlagMembers = EnumerateFlagMembers(app.GlobalOptionsModel).ToList();
		List<(string Segment, List<ParameterModel> Rows)> namespaceOptionSections = new();
		var namespaceOptionChain = GetCommandNamespaceOptionChain(app, cmd.RoutePrefix);
		var suppressedForNamespaceDisplay = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddCliKeys(globalFlagMembers, suppressedForNamespaceDisplay);
		foreach ((var seg, var gom) in namespaceOptionChain)
		{
			var allInNamespace = EnumerateFlagMembers(gom).ToList();
			var rows = allInNamespace.Where(p => !suppressedForNamespaceDisplay.Contains(p.CliLongName)).ToList();
			AddCliKeys(allInNamespace, suppressedForNamespaceDisplay);
			if (rows.Count > 0)
				namespaceOptionSections.Add((seg, rows));
		}

		var scopedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddCliKeys(globalFlagMembers, scopedKeys);
		foreach ((_, var gom) in namespaceOptionChain)
			AddCliKeys(EnumerateFlagMembers(gom), scopedKeys);

		var commandOnlyFlags = cmd.Parameters
			.Where(p => p.Kind == ParameterKind.Flag && !CommandFlagMatchesScopedKeys(p, scopedKeys))
			.ToList();

		var widthCandidates = new List<int> { "--help, -h".Length };
		widthCandidates.AddRange(globalFlagMembers.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		foreach ((_, var rows) in namespaceOptionSections)
			widthCandidates.AddRange(rows.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));

		widthCandidates.AddRange(commandOnlyFlags.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		var maxOptWidth = Math.Min(widthCandidates.Max(), 40);
		maxOptWidth = Math.Max(maxOptWidth, "--help, -h".Length);

		sb.AppendLine($"\t\tprivate static void PrintHelp_{cmd.RunMethodName}()");
		sb.AppendLine("\t\t{");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Usage: \") + CliHelpFormatting.Accent(\"{Escape(entryAssemblyName)}\") + \" {Escape(routeUsage)}{Escape(cmd.CommandName)} {Escape(cmd.UsageHints)}\");");

		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");

		EmitCommandHelpDocPrologue(sb, "\t\t\t", cmd.SummaryInnerXml, cmd.SummaryOneLiner, false);
		if (!string.IsNullOrWhiteSpace(cmd.SummaryOneLiner) || !string.IsNullOrWhiteSpace(cmd.SummaryInnerXml))
			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");

		var hasArgs = false;
		foreach (var p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Positional)
				hasArgs = true;
		}

		if (hasArgs)
		{
			var maxArgWidth = cmd.Parameters
				.Where(p => p.Kind == ParameterKind.Positional)
				.Select(p => (p.IsRequired ? $"<{p.CliLongName}>" : $"[<{p.CliLongName}>]").Length)
				.DefaultIfEmpty(0).Max();
			maxArgWidth = Math.Min(maxArgWidth, 40);

			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Arguments:\"));");
			foreach (var p in cmd.Parameters)
			{
				if (p.Kind != ParameterKind.Positional)
					continue;

				var nameCell = p.IsRequired
					? $"<{p.CliLongName}>"
					: $"[<{p.CliLongName}>]";
				var nameCellPadded = nameCell.PadRight(maxArgWidth);
				var desc = BuildDescriptionSuffix(p, forPositional: true);
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Placeholder(\"{Escape(nameCellPadded)}\")}}  {Escape(desc)}\");");
			}

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Global options:\"));");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(\"  \" + CliHelpFormatting.Placeholder(\"{Escape("--help, -h".PadRight(maxOptWidth))}\") + \"  Show help.\");");
		if (globalFlagMembers.Count > 0)
			EmitHelpOptionRows(sb, globalFlagMembers, maxOptWidth);

		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");

		foreach ((var segment, var gRows) in namespaceOptionSections)
		{
			sb.AppendLine($"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"'{Escape(segment)}' options:\"));");
			EmitHelpOptionRows(sb, gRows, maxOptWidth);
			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		if (commandOnlyFlags.Count > 0)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Options:\"));");
			EmitHelpOptionRows(sb, commandOnlyFlags, maxOptWidth);
		}

		var remarksXml = TransformRemarksInnerXmlForHelp(cmd.RemarksInnerXml, cmd, app.AllCommands, entryAssemblyName);
		var hasRemarks = !string.IsNullOrWhiteSpace(cmd.RemarksRendered) || !string.IsNullOrWhiteSpace(remarksXml);
		if (hasRemarks)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
			EmitNotesSection(sb, "\t\t\t", remarksXml, cmd.RemarksRendered);
		}

		if (!string.IsNullOrWhiteSpace(cmd.ExamplesRendered))
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Examples:\"));");
			foreach (var line in cmd.ExamplesRendered.Split('\n'))
			{
				var trimmed = line.TrimEnd('\r');
				if (trimmed.Length == 0)
					sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
				else
					sb.AppendLine($"\t\t\tConsole.Out.WriteLine(\"  {Escape(trimmed)}\");");
			}
		}

		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	private static string BuildDescriptionSuffix(ParameterModel p, bool forPositional)
	{
		var parts = new List<string>();

		if (!forPositional && p.Kind == ParameterKind.Flag && p.Special == BoolSpecialKind.None && p.IsRequired)
			parts.Add("[required]");

		if (!forPositional && p is { IsCollection: true, Kind: ParameterKind.Flag })
			parts.Add(p.CollectionSeparator is null ? "[repeatable]" : "[separated]");

		if (!string.IsNullOrWhiteSpace(p.Description))
			parts.Add(p.Description.Trim());

		if (p.ScalarKind == CliScalarKind.Enum && !p.EnumMemberNames.IsDefaultOrEmpty)
		{
			parts.Add("[values: " + string.Join(", ", p.EnumMemberNames) + "]");
			if (p.EnumMemberDocs is { Count: > 0 } docs)
			{
				var memberDescParts = new List<string>();
				foreach (var member in p.EnumMemberNames)
				{
					if (docs.TryGetValue(member, out var memberDoc) && !string.IsNullOrWhiteSpace(memberDoc))
						memberDescParts.Add($"{member}: {memberDoc.Trim()}");
				}
				if (memberDescParts.Count > 0)
					parts.Add("(" + string.Join("; ", memberDescParts) + ")");
			}
		}

		if (p.Special == BoolSpecialKind.None)
		{
			if (p.DefaultValueLiteral is not null)
				parts.Add($"[default: {FormatDefaultForHelp(p)}]");
		}

		return string.Join(" ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));
	}

	private static string FormatDefaultForHelp(ParameterModel p)
	{
		if (p.DefaultValueLiteral is null)
			return "";

		return p.TypeName switch
		{
			"string" => p.DefaultValueLiteral.Trim('"'),
			_ => p.DefaultValueLiteral
		};
	}

	/// <summary>
	/// Emits: if (string.Equals({varName}, "{value}", StringComparison.OrdinalIgnoreCase)) { {body} }
	/// </summary>
	private static void EmitOrdinalIgnoreCaseIf(
		StringBuilder sb,
		string indent,
		string varName,
		string value,
		Action<StringBuilder> body)
	{
		sb.AppendLine($"{indent}if (string.Equals({varName}, \"{Escape(value)}\", StringComparison.OrdinalIgnoreCase))");
		sb.AppendLine($"{indent}{{");
		body(sb);
		sb.AppendLine($"{indent}}}");
	}

	private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

	private static string EscapeDocXml(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

	/// <summary>
	/// Remarks XML only: <c>&lt;paramref name="x"/&gt;</c> for a CLI flag becomes <c>&lt;c&gt;--long-name&lt;/c&gt;</c>;
	/// <c>&lt;see cref="M:…"/&gt;</c> for another command handler becomes <c>&lt;c&gt;entryAsm route cmd usage-hints&lt;/c&gt;</c> (same tail as the emitted Usage line after the assembly name).
	/// </summary>
	private static string? TransformRemarksInnerXmlForHelp(
		string? innerXml,
		CommandModel forCommand,
		ImmutableArray<CommandModel> allCommands,
		string entryAssemblyName)
	{
		if (string.IsNullOrWhiteSpace(innerXml))
			return innerXml;

		var crefToCommand = new Dictionary<string, CommandModel>(StringComparer.Ordinal);
		foreach (var c in allCommands)
		{
			if (c.IsLambda || string.IsNullOrEmpty(c.HandlerDocCommentId))
				continue;
			if (crefToCommand.ContainsKey(c.HandlerDocCommentId))
				continue;
			crefToCommand[c.HandlerDocCommentId] = c;
		}

		var flagBySymbol = new Dictionary<string, ParameterModel>(StringComparer.Ordinal);
		foreach (var p in forCommand.Parameters)
		{
			if (p.Kind == ParameterKind.Flag)
				flagBySymbol[p.SymbolName] = p;
		}

		XElement root;
		try
		{
			root = XElement.Parse("<x>" + innerXml + "</x>", LoadOptions.PreserveWhitespace);
		}
		catch
		{
			return innerXml;
		}

		foreach (var e in root.Descendants().ToList())
		{
			if (e.Name.LocalName == "paramref")
			{
				var nameAttr = e.Attribute("name")?.Value;
				if (!string.IsNullOrEmpty(nameAttr) &&
				    flagBySymbol.TryGetValue(nameAttr!, out var pm))
					e.ReplaceWith(new XElement("c", "--" + pm.CliLongName));
				continue;
			}

			if (e.Name.LocalName != "see")
				continue;

			if (e.Attribute("langword") is not null || e.Attribute("href") is not null)
				continue;

			var crefAttr = e.Attribute("cref")?.Value;
			if (string.IsNullOrEmpty(crefAttr))
				continue;

			CommandModel? cmd = null;
			if (crefToCommand.TryGetValue(crefAttr!, out var byId))
				cmd = byId;
			else
			{
				foreach (var c in allCommands)
				{
					if (c.IsLambda || string.IsNullOrEmpty(c.HandlerDocCommentId))
						continue;
					if (!DocumentationCrefMatchesDocId(crefAttr!, c.HandlerDocCommentId))
						continue;
					cmd = c;
					break;
				}
			}

			if (cmd is not null)
				e.ReplaceWith(new XElement("c", BuildCommandUsageSynopsisTail(cmd, entryAssemblyName)));
		}

		return string.Concat(root.Nodes().Select(n => n.ToString()));
	}

	private static string BuildCommandUsageSynopsisTail(CommandModel cmd, string entryAssemblyName)
	{
		var routeUsage = cmd.RoutePrefix.IsDefaultOrEmpty
			? ""
			: string.Join(" ", cmd.RoutePrefix) + " ";
		return $"{entryAssemblyName} {routeUsage}{cmd.CommandName} {cmd.UsageHints}".TrimEnd();
	}

	/// <summary>
	/// <see cref="IMethodSymbol.GetDocumentationCommentId"/> vs XML <c>cref</c>: compiler XML may use the full <c>M:…</c> id or a short form (e.g. <c>Type.Method</c>).
	/// </summary>
	private static bool DocumentationCrefMatchesMethod(string cref, IMethodSymbol method)
	{
		if (string.IsNullOrEmpty(cref))
			return false;

		cref = cref.Replace("global::", "");

		if (method.GetDocumentationCommentId() is not { Length: > 0 } fullId)
			return false;

		return DocumentationCrefMatchesDocId(cref, fullId);
	}

	/// <summary>String-based version of <see cref="DocumentationCrefMatchesMethod"/> that takes the pre-extracted doc comment id.</summary>
	private static bool DocumentationCrefMatchesDocId(string cref, string fullId)
	{
		if (string.IsNullOrEmpty(cref) || string.IsNullOrEmpty(fullId))
			return false;

		cref = cref.Replace("global::", "");
		fullId = fullId.Replace("global::", "");

		if (string.Equals(cref, fullId, StringComparison.Ordinal))
			return true;

		if (!fullId.StartsWith("M:", StringComparison.Ordinal) || fullId.Length < 3)
			return false;

		var sigParen = fullId.IndexOf('(', 2);
		var qualifiedMember = sigParen >= 2 ? fullId.Substring(2, sigParen - 2) : fullId.Substring(2);

		if (string.Equals(cref, qualifiedMember, StringComparison.Ordinal))
			return true;

		// e.g. cref "CliRegistrationModule.DocLambdaEcho" or "Demo" for "…DocsCommands.Demo(…)".
		if (qualifiedMember.EndsWith(cref, StringComparison.Ordinal))
			return true;

		return false;
	}

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
		string LambdaDelegateFq = "")
	{
		public static CommandModel FromRootMethod(
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			ImmutableArray<string> routePrefix,
			SourceProductionContext context,
			Location diagnosticLocation)
		{
			var parameters = BuildParameterModels(method, parseOptions, context, diagnosticLocation);
			ReportDuplicateCliNames(context, diagnosticLocation, parameters);
			ValidateExpandedParameterLayout(context, diagnosticLocation, parameters);
			foreach (var p in parameters)
			{
				if (p.IsCollection && p.Kind == ParameterKind.Positional)
					context.ReportDiagnostic(Diagnostic.Create(CollectionPositionalNotSupported, diagnosticLocation));
			}

			var docs = MergeMethodDocumentationFromTrivia(
				method,
				Documentation.ParseMethod(method.GetDocumentationCommentXml(), parseOptions),
				parseOptions);

			var withDocs = ApplyParamDocumentation(parameters, method, docs.ParamDocsRaw);
			withDocs = ApplyCollectionSeparatorsFromDocumentation(withDocs, method, docs.ParamSeparators);
			var usage = UsageSynopsis.Build(withDocs);
			var runName = BuildRootDefaultRunMethodName(routePrefix);
			var containingFq = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			var hasParamlessCtor = method.ContainingType is INamedTypeSymbol namedCt &&
			                       HasPublicParameterlessCtor(namedCt);
			var (retFq, retIsAsync, retIsVoid, handlerNoInj, handlerParams, handlerLoc, ctorParams, mwData, docId) =
				ExtractHandlerAnalysis(method);
			return new CommandModel(
				routePrefix,
				RootDefaultInternalCommandName,
				runName,
				containingFq,
				method.Name,
				!method.IsStatic,
				hasParamlessCtor,
				retFq,
				retIsAsync,
				retIsVoid,
				withDocs,
				handlerNoInj,
				handlerParams,
				handlerLoc,
				ctorParams,
				docId,
				docs.SummaryOneLiner,
				docs.RemarksRendered,
				docs.SummaryInnerXml,
				docs.RemarksInnerXml,
				docs.ExamplesRendered,
				usage,
				mwData,
				IsRootDefault: true);
		}

		public static CommandModel FromMethod(
			string commandName,
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			ImmutableArray<string> routePrefix,
			SourceProductionContext context,
			Location diagnosticLocation)
		{
			var parameters = BuildParameterModels(method, parseOptions, context, diagnosticLocation);
			ReportDuplicateCliNames(context, diagnosticLocation, parameters);
			ValidateExpandedParameterLayout(context, diagnosticLocation, parameters);
			foreach (var p in parameters)
			{
				if (p.IsCollection && p.Kind == ParameterKind.Positional)
					context.ReportDiagnostic(Diagnostic.Create(CollectionPositionalNotSupported, diagnosticLocation));
			}

			var docs = MergeMethodDocumentationFromTrivia(
				method,
				Documentation.ParseMethod(method.GetDocumentationCommentXml(), parseOptions),
				parseOptions);
			var withDocs = ApplyParamDocumentation(parameters, method, docs.ParamDocsRaw);
			withDocs = ApplyCollectionSeparatorsFromDocumentation(withDocs, method, docs.ParamSeparators);
			var usage = UsageSynopsis.Build(withDocs);
			var runName = BuildRunMethodName(routePrefix, commandName);
			var containingFq = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			var hasParamlessCtor = method.ContainingType is INamedTypeSymbol namedCt &&
			                       HasPublicParameterlessCtor(namedCt);
			var (retFq, retIsAsync, retIsVoid, handlerNoInj, handlerParams, handlerLoc, ctorParams, mwData, docId) =
				ExtractHandlerAnalysis(method);
			return new CommandModel(
				routePrefix,
				commandName,
				runName,
				containingFq,
				method.Name,
				!method.IsStatic,
				hasParamlessCtor,
				retFq,
				retIsAsync,
				retIsVoid,
				withDocs,
				handlerNoInj,
				handlerParams,
				handlerLoc,
				ctorParams,
				docId,
				docs.SummaryOneLiner,
				docs.RemarksRendered,
				docs.SummaryInnerXml,
				docs.RemarksInnerXml,
				docs.ExamplesRendered,
				usage,
				mwData);
		}

		/// <summary>Overload for the per-invocation Select step — uses <see cref="DiagnosticAccumulator"/> instead of SourceProductionContext.</summary>
		public static CommandModel FromRootMethod(
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			ImmutableArray<string> routePrefix,
			DiagnosticAccumulator acc,
			Location diagnosticLocation)
		{
			var parameters = BuildParameterModels(method, parseOptions, acc, diagnosticLocation);
			ReportDuplicateCliNamesAcc(acc, diagnosticLocation, parameters);
			ValidateExpandedParameterLayoutAcc(acc, diagnosticLocation, parameters);
			foreach (var p in parameters)
				if (p.IsCollection && p.Kind == ParameterKind.Positional)
					acc.Add(CollectionPositionalNotSupported, diagnosticLocation);
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
			Location diagnosticLocation)
		{
			var parameters = BuildParameterModels(method, parseOptions, acc, diagnosticLocation);
			ReportDuplicateCliNamesAcc(acc, diagnosticLocation, parameters);
			ValidateExpandedParameterLayoutAcc(acc, diagnosticLocation, parameters);
			foreach (var p in parameters)
				if (p.IsCollection && p.Kind == ParameterKind.Positional)
					acc.Add(CollectionPositionalNotSupported, diagnosticLocation);
			var docs = MergeMethodDocumentationFromTrivia(method, Documentation.ParseMethod(method.GetDocumentationCommentXml(), parseOptions), parseOptions);
			var withDocs = ApplyParamDocumentation(parameters, method, docs.ParamDocsRaw);
			withDocs = ApplyCollectionSeparatorsFromDocumentation(withDocs, method, docs.ParamSeparators);
			var usage = UsageSynopsis.Build(withDocs);
			var runName = BuildRunMethodName(routePrefix, commandName);
			var containingFq = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			var hasParamlessCtor = method.ContainingType is INamedTypeSymbol namedCt && HasPublicParameterlessCtor(namedCt);
			var (retFq, retIsAsync, retIsVoid, handlerNoInj, handlerParams, handlerLoc, ctorParams, mwData, docId) = ExtractHandlerAnalysis(method);
			return new CommandModel(routePrefix, commandName, runName, containingFq, method.Name, !method.IsStatic, hasParamlessCtor, retFq, retIsAsync, retIsVoid, withDocs, handlerNoInj, handlerParams, handlerLoc, ctorParams, docId, docs.SummaryOneLiner, docs.RemarksRendered, docs.SummaryInnerXml, docs.RemarksInnerXml, docs.ExamplesRendered, usage, mwData);
		}

		private static ImmutableArray<ParameterModel> BuildParameterModels(
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			DiagnosticAccumulator acc,
			Location diagnosticLocation)
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
					foreach (var pm in FlattenAsParametersTypeAcc(acc, diagnosticLocation, p, namedType, prefix, parseOptions))
						builder.Add(pm);
					continue;
				}
				builder.Add(ParameterModel.From(p));
			}
			return builder.ToImmutable();
		}

		private static ImmutableArray<ParameterModel> BuildParameterModels(
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			SourceProductionContext context,
			Location diagnosticLocation)
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
					foreach (var pm in FlattenAsParametersType(context, diagnosticLocation, p, namedType, prefix, parseOptions))
						builder.Add(pm);
					continue;
				}

				builder.Add(ParameterModel.From(p));
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
			var retIsVoid = retFq is "global::System.Void"
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
				paramBuilder.Add(new HandlerParam(p.Name, GetMetadataName(p.Type), isInj, isAsParam, asParamPrefix, asParamTypeFq, asParamIsPublic, asParamIsGeneric, asParamBestCtor));
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
		string? ElementParserTypeFq = null,
		string? ElementCustomValueTypeFq = null,
		string? FullDeclaredTypeFq = null,
		string? AsParametersOwnerParamName = null,
		int AsParametersMemberOrder = -1,
		string? AsParametersTypeFq = null,
		bool AsParametersUseInit = false,
		string? AsParametersClrName = null,
		bool CollectionTargetIsArray = false,
		ImmutableDictionary<string, string>? EnumMemberDocs = null)
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
			AsParametersMeta? asParams)
		{
			ClassifyScalarForType(elementType, attributeHost, BoolSpecialKind.None,
				out var elemSk, out var elemTn, out var eFq, out var eMem, out var pFq, out var cFq);
			var sep = TryGetCollectionSeparatorFromAttribute(attributeHost);
			var required = isSeparateType
				? ComputeRequiredForOptionsType(collectionType, BoolSpecialKind.None)
				: ComputeRequired((IParameterSymbol)attributeHost, BoolSpecialKind.None);
			var fq = collectionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
				null,
				ImmutableArray<string>.Empty,
				IsCollection: true,
				CollectionSeparator: sep,
				ElementScalarKind: elemSk,
				ElementTypeName: elemTn,
				ElementEnumTypeFq: eFq,
				ElementEnumMemberNames: eMem,
				ElementParserTypeFq: pFq,
				ElementCustomValueTypeFq: cFq,
				FullDeclaredTypeFq: fq,
				CollectionTargetIsArray: collectionType is IArrayTypeSymbol,
				AsParametersOwnerParamName: asParams?.OwnerParamName,
				AsParametersMemberOrder: asParams?.MemberOrder ?? -1,
				AsParametersTypeFq: asParams?.TypeFq,
				AsParametersUseInit: asParams?.UseInit ?? false,
				AsParametersClrName: asParams?.ClrName);
		}

		// ── five factory methods ─────────────────────────────────────────────

		public static ParameterModel From(IParameterSymbol p)
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
			if (TryUnwrapCollectionType(p.Type, out var elemType) && bs == BoolSpecialKind.None)
			{
				var defLitColl = TryGetDefaultLiteral(p, BoolSpecialKind.None);
				return BuildCollectionParameterModel(p.Type, elemType, p, kind,
					Naming.ToCliLongName(p.Name), SafeLocalName(p.Name), p.Name,
					isSeparateType: false, defLitColl, "", asParams: null);
			}

			ClassifyScalarUnified(p.Type, p, bs, isSeparateType: false,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			var required = ComputeRequired(p, bs);
			var defLit = TryGetDefaultLiteral(p, bs);
			var enumDocs = sk == CliScalarKind.Enum ? TryGetEnumDocs(p.Type) : null;
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
				EnumMemberDocs: enumDocs);
		}

		public static ParameterModel FromOptionsProperty(IPropertySymbol prop)
		{
			var bs = ClassifyBool(prop.Type);
			if (TryUnwrapCollectionType(prop.Type, out var elemType) && bs == BoolSpecialKind.None)
			{
				return BuildCollectionParameterModel(prop.Type, elemType, prop, ParameterKind.Flag,
					Naming.ToCliLongName(prop.Name), SafeLocalName(prop.Name), prop.Name,
					isSeparateType: true, defaultLiteral: null, "", asParams: null);
			}

			ClassifyScalarUnified(prop.Type, prop, bs, isSeparateType: true,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			var required = ComputeRequiredForOptionsType(prop.Type, bs);
			var enumDocs = sk == CliScalarKind.Enum ? TryGetEnumDocs(prop.Type) : null;
			return new ParameterModel(
				prop.Name,
				SafeLocalName(prop.Name),
				Naming.ToCliLongName(prop.Name),
				ParameterKind.Flag,
				bs,
				sk,
				typeName,
				enumFq,
				enumMembers,
				parserFq,
				customValFq,
				required,
				null,
				"",
				null,
				ImmutableArray<string>.Empty,
				EnumMemberDocs: enumDocs);
		}

		public static ParameterModel FromOptionsField(IFieldSymbol field)
		{
			var bs = ClassifyBool(field.Type);
			if (TryUnwrapCollectionType(field.Type, out var elemType) && bs == BoolSpecialKind.None)
			{
				return BuildCollectionParameterModel(field.Type, elemType, field, ParameterKind.Flag,
					Naming.ToCliLongName(field.Name), SafeLocalName(field.Name), field.Name,
					isSeparateType: true, defaultLiteral: null, "", asParams: null);
			}

			ClassifyScalarUnified(field.Type, field, bs, isSeparateType: true,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			var required = ComputeRequiredForOptionsType(field.Type, bs);
			return new ParameterModel(
				field.Name,
				SafeLocalName(field.Name),
				Naming.ToCliLongName(field.Name),
				ParameterKind.Flag,
				bs,
				sk,
				typeName,
				enumFq,
				enumMembers,
				parserFq,
				customValFq,
				required,
				null,
				"",
				null,
				ImmutableArray<string>.Empty);
		}
		public static ParameterModel FromAsParametersCtorParameter(
			string methodParamName,
			string typeFq,
			INamedTypeSymbol containingType,
			IParameterSymbol cp,
			string namePrefix,
			int memberOrder,
			CSharpParseOptions parseOptions)
		{
			var isArg = HasArgumentAttribute(cp);
			var kind = isArg ? ParameterKind.Positional : ParameterKind.Flag;
			var bs = ClassifyBool(cp.Type);
			var cli = namePrefix + Naming.ToCliLongName(cp.Name);
			var local = SafeLocalName(methodParamName + "_" + cp.Name);
			var desc = Documentation.GetParamDocFromType(containingType, cp.Name);
			var meta = new AsParametersMeta(methodParamName, memberOrder, typeFq, UseInit: false, cp.Name);
			if (TryUnwrapCollectionType(cp.Type, out var elemType) && bs == BoolSpecialKind.None)
			{
				var defLitColl = TryGetDefaultLiteral(cp, BoolSpecialKind.None);
				return BuildCollectionParameterModel(cp.Type, elemType, cp, kind, cli, local, cp.Name,
					isSeparateType: false, defLitColl, desc, meta);
			}

			ClassifyScalarUnified(cp.Type, cp, bs, isSeparateType: false,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			var required = ComputeRequired(cp, bs);
			var defLit = TryGetDefaultLiteral(cp, bs);
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
				AsParametersOwnerParamName: methodParamName,
				AsParametersMemberOrder: memberOrder,
				AsParametersTypeFq: typeFq,
				AsParametersUseInit: false,
				AsParametersClrName: cp.Name);
		}

		public static ParameterModel FromAsParametersInitProperty(
			string methodParamName,
			string typeFq,
			IPropertySymbol prop,
			string namePrefix,
			int memberOrder,
			CSharpParseOptions parseOptions)
		{
			var isArg = HasArgumentAttribute(prop);
			var kind = isArg ? ParameterKind.Positional : ParameterKind.Flag;
			var bs = ClassifyBool(prop.Type);
			var cli = namePrefix + Naming.ToCliLongName(prop.Name);
			var local = SafeLocalName(methodParamName + "_" + prop.Name);
			var desc = Documentation.GetPropertySummaryLine(prop);
			var meta = new AsParametersMeta(methodParamName, memberOrder, typeFq, UseInit: true, prop.Name);
			if (TryUnwrapCollectionType(prop.Type, out var elemType) && bs == BoolSpecialKind.None)
			{
				return BuildCollectionParameterModel(prop.Type, elemType, prop, kind, cli, local, prop.Name,
					isSeparateType: true, defaultLiteral: null, desc, meta);
			}

			ClassifyScalarUnified(prop.Type, prop, bs, isSeparateType: true,
				out var sk, out var typeName, out var enumFq, out var enumMembers, out var parserFq, out var customValFq);
			var required = ComputeRequiredForOptionsType(prop.Type, bs);
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
				null,
				desc,
				null,
				ImmutableArray<string>.Empty,
				AsParametersOwnerParamName: methodParamName,
				AsParametersMemberOrder: memberOrder,
				AsParametersTypeFq: typeFq,
				AsParametersUseInit: true,
				AsParametersClrName: prop.Name);
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

		private static bool IsInjectedStatic(IParameterSymbol p)
		{
			if (p.Type is not INamedTypeSymbol named)
				return false;

			var fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			return fq == "global::System.Threading.CancellationToken";
		}

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
			return k;
		}
	}

	private enum ParameterKind
	{
		Flag,
		Positional,
		Injected,
		/// <summary>
		/// A flattened member from a global or namespace options type injected into this command.
		/// Participates in bool-switch / short-opt / canon-name detection so the flag is parsed correctly,
		/// but is skipped by value-declaration and binding emission (the value is obtained from a
		/// locally-reconstructed options instance instead).
		/// </summary>
		OptionsInjected
	}

	private enum CliScalarKind
	{
		Primitive,
		Enum,
		FileInfo,
		DirectoryInfo,
		Uri,
		CustomParser,
		Collection
	}

	private enum BoolSpecialKind
	{
		None,
		Bool,
		NullableBool
	}

	private static class Naming
	{
		public static string ToCommandName(string name) => ToKebabCase(StripCommandSuffixes(name));

		public static string ToCliLongName(string name) => ToKebabCase(name);

		public static string ToTypeSegmentName(string typeName) => ToKebabCase(StripCommandSuffixes(typeName));

		public static string SanitizeIdentifier(string commandName)
		{
			var sb = new StringBuilder();
			foreach (var c in commandName)
			{
				if (char.IsLetterOrDigit(c))
					sb.Append(c);
				else
					sb.Append('_');
			}

			return sb.Length == 0 ? "cmd" : sb.ToString();
		}

		private static string StripCommandSuffixes(string typeName)
		{
			string[] suffixes = ["Commands", "Command", "Handlers", "Handler"];
			foreach (var s in suffixes)
			{
				if (typeName.EndsWith(s, StringComparison.Ordinal) && typeName.Length > s.Length)
					return typeName.Substring(0, typeName.Length - s.Length);
			}

			return typeName;
		}

		private static string ToKebabCase(string name)
		{
			if (string.IsNullOrEmpty(name))
				return name;

			var sb = new StringBuilder();
			for (var i = 0; i < name.Length; i++)
			{
				var c = name[i];
				if (char.IsUpper(c))
				{
					if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
						sb.Append('-');
					sb.Append(char.ToLowerInvariant(c));
				}
				else
					sb.Append(c);
			}

			return sb.ToString();
		}
	}

	private readonly record struct ParamDoc(char? ShortOpt, ImmutableArray<string> Aliases, string Description);

	private static class ParamDocParser
	{
		public static ParamDoc Parse(string text)
		{
			text = text.Trim();
			if (text.Length == 0)
				return new ParamDoc(null, ImmutableArray<string>.Empty, "");

			var parts = text.Split(',');
			char? shortOpt = null;
			var aliases = ImmutableArray.CreateBuilder<string>();
			var i = 0;
			for (; i < parts.Length; i++)
			{
				var seg = parts[i].Trim();
				if (seg.Length == 0)
				{
					i++;
					break;
				}

				if (LooksLikeShortFlag(seg))
				{
					if (shortOpt is null)
						shortOpt = seg[1];
					continue;
				}

				if (LooksLikeLongFlag(seg))
				{
					aliases.Add(seg.Substring(2));
					continue;
				}

				break;
			}

			var desc = i >= parts.Length ? "" : string.Join(",", parts, i, parts.Length - i).Trim();
			return new ParamDoc(shortOpt, aliases.ToImmutable(), desc);
		}

		private static bool LooksLikeShortFlag(string seg) =>
			seg.Length == 2 && seg[0] == '-' && seg[1] != '-' && (char.IsLetterOrDigit(seg[1]));

		private static bool LooksLikeLongFlag(string seg) =>
			seg.Length > 2 && seg.StartsWith("--", StringComparison.Ordinal);
	}

	private static class HelpLayout
	{
		public static string FormatOptionLeftCell(ParameterModel p)
		{
			if (p.Special == BoolSpecialKind.Bool)
				return "--" + p.CliLongName;

			if (p.Special == BoolSpecialKind.NullableBool)
				return "--" + p.CliLongName + " / --no-" + p.CliLongName;

			var th = TypeHint(p);
			var sb = new StringBuilder();
			if (p.ShortOpt is char ch)
			{
				sb.Append('-').Append(ch).Append(", ");
			}

			foreach (var a in p.Aliases)
			{
				if (string.Equals(a, p.CliLongName, StringComparison.OrdinalIgnoreCase))
					continue;
				sb.Append("--").Append(a).Append(", ");
			}

			sb.Append("--").Append(p.CliLongName);
			if (p.Special == BoolSpecialKind.None)
				sb.Append(' ').Append(th);

			return sb.ToString();
		}

		public static string TypeHint(ParameterModel p)
		{
			switch (p.ScalarKind)
			{
				case CliScalarKind.Collection:
					return "<values>";
				case CliScalarKind.Enum:
					return "<string>";
				case CliScalarKind.FileInfo:
					return "<path>";
				case CliScalarKind.DirectoryInfo:
					return "<dir>";
				case CliScalarKind.Uri:
					return "<uri>";
				case CliScalarKind.CustomParser:
					return "<value>";
				default:
					break;
			}

			return p.TypeName switch
			{
				"string" => "<string>",
				"int" => "<int>",
				"long" => "<long>",
				"float" => "<float>",
				"double" => "<double>",
				"decimal" => "<decimal>",
				"bool" => "<bool>",
				"bool?" => "<bool?>",
				_ => "<value>"
			};
		}
	}

	private readonly record struct MethodDocumentation(
		string SummaryOneLiner,
		string RemarksRendered,
		string ExamplesRendered,
		string SummaryInnerXml,
		string RemarksInnerXml,
		ImmutableDictionary<string, string> ParamDocsRaw,
		ImmutableDictionary<string, string> ParamSeparators);

	private static class Documentation
	{
		public static MethodDocumentation ParseMethod(string? xml, CSharpParseOptions parseOptions)
		{
			if (string.IsNullOrWhiteSpace(xml))
				return new MethodDocumentation("", "", "", "", "", ImmutableDictionary<string, string>.Empty,
					ImmutableDictionary<string, string>.Empty);

			try
			{
				var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
				var root = doc.Root;
				if (root is null)
					return new MethodDocumentation("", "", "", "", "", ImmutableDictionary<string, string>.Empty,
						ImmutableDictionary<string, string>.Empty);

				var summary = WhitespaceCollapsePattern.Replace(FlattenBlock(root.Element("summary")).Replace("\r\n", "\n"), " ").Trim();
				var remarks = FlattenBlock(root.Element("remarks")).Replace("\r\n", "\n").Trim();
				var summaryInner = GetElementInnerXml(root.Element("summary"));
				var remarksInner = GetElementInnerXml(root.Element("remarks"));
				var examples = string.Join("\n\n", root.Elements("example")
					.Select(e => FlattenBlock(e).Replace("\r\n", "\n").Trim())
					.Where(s => !string.IsNullOrWhiteSpace(s)));
				var paramMap =
					ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
				var sepMap =
					ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
				foreach (var pe in root.Elements("param"))
				{
					var name = pe.Attribute("name")?.Value;
					if (string.IsNullOrEmpty(name))
						continue;

					var sepEl = pe.Elements().FirstOrDefault(e => e.Name.LocalName == "separator");
					if (sepEl is not null && !string.IsNullOrEmpty(sepEl.Value))
						sepMap[name!] = sepEl.Value.Trim();

					paramMap[name!] = FlattenParam(pe);
				}

				return new MethodDocumentation(summary, remarks, examples, summaryInner, remarksInner, paramMap.ToImmutable(), sepMap.ToImmutable());
			}
			catch
			{
				return new MethodDocumentation("", "", "", "", "", ImmutableDictionary<string, string>.Empty,
					ImmutableDictionary<string, string>.Empty);
			}
		}

		private static string GetElementInnerXml(XElement? el)
		{
			if (el is null)
				return "";
			return string.Concat(el.Nodes().Select(n => n.ToString()));
		}

		public static string GetParamDocFromType(INamedTypeSymbol type, string parameterName)
		{
			var xml = type.GetDocumentationCommentXml();
			if (string.IsNullOrWhiteSpace(xml))
				return "";
			try
			{
				var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
				var root = doc.Root;
				if (root is null)
					return "";
				foreach (var pe in root.Elements("param"))
				{
					if (string.Equals(pe.Attribute("name")?.Value, parameterName, StringComparison.Ordinal))
						return FlattenParam(pe);
				}
			}
			catch
			{
				// ignore
			}

			return "";
		}

		public static string GetPropertySummaryLine(IPropertySymbol prop)
		{
			var xml = prop.GetDocumentationCommentXml();
			if (string.IsNullOrWhiteSpace(xml))
				return "";
			try
			{
				var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
				var root = doc.Root;
				if (root is null)
					return "";
				return FlattenBlock(root.Element("summary")).Replace("\r\n", "\n").Trim();
			}
			catch
			{
				return "";
			}
		}

		/// <summary>First line of <c>&lt;summary&gt;</c> for a type symbol (handles <c>&lt;member&gt;</c>-wrapped XML from Roslyn).</summary>
		public static string GetTypeSummaryLine(string? xml)
		{
			if (string.IsNullOrWhiteSpace(xml))
				return "";
			try
			{
				var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
				var root = doc.Root;
				if (root is null)
					return "";
				var sum = root.Element("summary");
				if (sum is null)
				{
					foreach (var e in root.Descendants())
					{
						if (e.Name.LocalName == "summary")
						{
							sum = e;
							break;
						}
					}
				}

				if (sum is null)
					return "";
				return FlattenBlock(sum).Replace("\r\n", "\n").Trim();
			}
			catch
			{
				return "";
			}
		}

		/// <summary>Inner XML of <c>&lt;summary&gt;</c> and <c>&lt;remarks&gt;</c> for a type symbol.</summary>
		public static (string SummaryInnerXml, string RemarksInnerXml) GetTypeDocumentation(string? xml)
		{
			if (string.IsNullOrWhiteSpace(xml))
				return ("", "");
			try
			{
				var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
				var root = doc.Root;
				if (root is null)
					return ("", "");
				// Roslyn wraps type XML in a <member> element
				var search = root.Element("member") ?? root;
				return (GetElementInnerXml(search.Element("summary")), GetElementInnerXml(search.Element("remarks")));
			}
			catch
			{
				return ("", "");
			}
		}

		private static string FlattenParam(XElement param)
		{
			var sb = new StringBuilder();
			foreach (var n in param.Nodes())
			{
				if (n is XElement e && e.Name.LocalName == "separator")
					continue;
				FlattenNodes(new[] { n }, sb);
			}

			return sb.ToString().Trim();
		}

		public static string FlattenBlockPublic(XElement? element) => FlattenBlock(element);

		private static string FlattenBlock(XElement? element)
		{
			if (element is null)
				return "";

			var sb = new StringBuilder();
			FlattenNodes(element.Nodes(), sb);
			return sb.ToString();
		}

		private static void FlattenNodes(IEnumerable<XNode> nodes, StringBuilder sb)
		{
			foreach (var n in nodes)
			{
				switch (n)
				{
					case XText t:
						sb.Append(t.Value);
						break;
					case XElement e when e.Name.LocalName == "para":
						if (sb.Length > 0)
							sb.AppendLine();
						FlattenNodes(e.Nodes(), sb);
						break;
					case XElement e when e.Name.LocalName == "code":
						sb.AppendLine();
						foreach (var c in e.Nodes())
						{
							if (c is XText tx)
								sb.Append("    ").AppendLine(tx.Value.TrimEnd());
						}

						break;
					case XElement e when e.Name.LocalName == "list":
						if (sb.Length > 0)
							sb.AppendLine();
						foreach (var item in e.Elements().Where(x => x.Name.LocalName == "item"))
						{
							sb.Append("  - ");
							var desc = item.Element("description");
							if (desc is not null)
								FlattenNodes(desc.Nodes(), sb);
							else
								FlattenNodes(item.Nodes(), sb);
							sb.AppendLine();
						}

						break;
					case XElement e when e.Name.LocalName == "c":
						sb.Append(e.Value.Trim());
						break;
					case XElement e when e.Name.LocalName == "paramref":
					{
						var pn = e.Attribute("name")?.Value;
						if (!string.IsNullOrEmpty(pn))
							sb.Append(pn);
						break;
					}
					case XElement e when e.Name.LocalName == "typeparamref":
					{
						var tn = e.Attribute("name")?.Value;
						if (!string.IsNullOrEmpty(tn))
							sb.Append(tn);
						break;
					}
					case XElement e when e.Name.LocalName == "see":
						AppendSeeForListing(e, sb);
						break;
					case XElement e:
						FlattenNodes(e.Nodes(), sb);
						break;
				}
			}
		}

		private static void AppendSeeForListing(XElement e, StringBuilder sb)
		{
			var lang = e.Attribute("langword")?.Value;
			if (!string.IsNullOrEmpty(lang))
			{
				sb.Append(lang);
				return;
			}

			var href = e.Attribute("href")?.Value;
			if (!string.IsNullOrEmpty(href))
			{
				var vis = string.IsNullOrWhiteSpace(e.Value) ? href! : e.Value.Trim();
				sb.Append(vis);
				return;
			}

			var cref = e.Attribute("cref")?.Value;
			if (!string.IsNullOrEmpty(cref))
			{
				var vis = string.IsNullOrWhiteSpace(e.Value) ? CrefShortNameForListing(cref!) : e.Value.Trim();
				sb.Append(vis);
				return;
			}

			FlattenNodes(e.Nodes(), sb);
		}

		private static string CrefShortNameForListing(string cref)
		{
			if (string.IsNullOrEmpty(cref))
				return "";
			var colon = cref.IndexOf(':');
			var tail = colon >= 0 ? cref.Substring(colon + 1) : cref;
			var dot = tail.LastIndexOf('.');
			var name = dot >= 0 ? tail.Substring(dot + 1) : tail;
			var paren = name.IndexOf('(');
			if (paren >= 0)
				name = name.Substring(0, paren);
			return name;
		}
	}

	private static class UsageSynopsis
	{
		/// <summary>Minimal usage tail: required flags and positionals explicitly; optional switches and flags fold into a single <c>[options]</c>.</summary>
		public static string Build(ImmutableArray<ParameterModel> parameters)
		{
			var parts = new List<string>();
			var needsOptions = false;

			foreach (var p in parameters)
			{
				if (p.Kind == ParameterKind.Injected || p.Kind == ParameterKind.OptionsInjected)
					continue;

				if (p.Kind == ParameterKind.Positional)
				{
					var seg = p.IsRequired ? $"<{p.CliLongName}>" : $"[<{p.CliLongName}>]";
					parts.Add(seg);
					continue;
				}

				if (p.Kind != ParameterKind.Flag)
					continue;

				if (p.Special == BoolSpecialKind.Bool)
				{
					needsOptions = true;
					continue;
				}

				if (p.Special == BoolSpecialKind.NullableBool)
				{
					needsOptions = true;
					continue;
				}

				if (p.IsCollection)
				{
					if (p.IsRequired)
					{
						var typeHint = HelpLayout.TypeHint(p);
						parts.Add($"--{p.CliLongName} {typeHint}");
					}
					else
					{
						needsOptions = true;
					}

					continue;
				}

				var typeHintScalar = HelpLayout.TypeHint(p);
				if (p.IsRequired)
					parts.Add($"--{p.CliLongName} {typeHintScalar}");
				else
					needsOptions = true;
			}

			if (needsOptions)
				parts.Add("[options]");

			return string.Join(" ", parts);
		}
	}
}
