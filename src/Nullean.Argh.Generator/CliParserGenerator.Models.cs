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
	private sealed class RegistryNode
	{
		public CommandModel? RootCommand;
		/// <summary>Pointer to a named command that acts as the scope's root alias (set by <c>MapAndRootAlias&lt;T&gt;</c>).</summary>
		public CommandModel? RootAlias;
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
		public string RootSummary = "";
		public string? SchemaVersionOverride;
		public readonly RegistryNode Root = new();
		public ImmutableArray<CommandModel> AllCommands = ImmutableArray<CommandModel>.Empty;
		public ImmutableArray<GlobalMiddlewareRegistration> GlobalMiddleware = ImmutableArray<GlobalMiddlewareRegistration>.Empty;
		public readonly List<ArglessNamespaceCodegenEntry> ArglessNamespaceCodegen = new();
		public ImmutableArray<EnvVarDocEntry> EnvironmentVars = ImmutableArray<EnvVarDocEntry>.Empty;
		public ImmutableArray<ConfigFileDocEntry> ConfigFiles = ImmutableArray<ConfigFileDocEntry>.Empty;
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
		/// <summary>All ancestor metadata names of this parameter's type — used for subclass-aware options injection matching.</summary>
		ImmutableArray<string> TypeAllBaseTypeMetadataNames,
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
	private sealed record AIUseGlobalOptions(string FilePath, int SpanStart, OptionsTypeModel Model)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>A <c>CommandNamespaceOptions&lt;T&gt;()</c> invocation — only valid inside a namespace.</summary>
	private sealed record AIUseNamespaceOptions(string FilePath, int SpanStart, OptionsTypeModel Model)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>A <c>UseMiddleware&lt;T&gt;()</c> invocation — only valid at root scope.</summary>
	private sealed record AIUseMiddleware(string FilePath, int SpanStart, GlobalMiddlewareRegistration Registration)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>A <c>UseCliDescription(string)</c> invocation — only meaningful at root scope.</summary>
	private sealed record AIUseCliDescription(string FilePath, int SpanStart, string Description)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>A <c>UseSchemaVersion(string)</c> invocation — overrides the <c>version</c> field in the <c>__schema</c> document.</summary>
	private sealed record AIUseSchemaVersion(string FilePath, int SpanStart, string Version)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>A <c>DocumentEnvironmentVariables(...)</c> invocation — only meaningful at root scope.</summary>
	private sealed record AIDocumentEnvironmentVariables(
		string FilePath,
		int SpanStart,
		ImmutableArray<EnvVarDocEntry> Variables,
		ImmutableArray<ConfigFileDocEntry> ConfigFiles)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>Symbol-free representation of a <see cref="CliEnvVar"/> passed to <c>DocumentEnvironmentVariables</c>.</summary>
	private sealed record EnvVarDocEntry(string Name, string? Description, bool Required, string? DefaultValue);

	/// <summary>Symbol-free representation of a <see cref="CliConfigFile"/> passed to <c>DocumentEnvironmentVariables</c>.</summary>
	private sealed record ConfigFileDocEntry(string Path, string? Description, bool Required);

	/// <summary>Symbol-free intent data extracted from <c>[CommandIntent]</c>.</summary>
	private sealed record CommandIntentData(bool? Destructive, bool? Idempotent, string? Scope, bool? RequiresConfirmation, bool? RequiresAuth);

	/// <summary>Symbol-free output data extracted from <c>[CommandOutput]</c>.</summary>
	private sealed record CommandOutputData(ImmutableArray<string> Formats, string? FormatFlag);

	/// <summary>
	/// An <c>Add&lt;T&gt;()</c> or <c>Add(name, handler)</c> invocation.
	/// For <c>Add&lt;T&gt;</c>, <see cref="TypeSnapshot"/> holds the full registry structure;
	/// for <c>Add(name, handler)</c>, <see cref="Commands"/> holds the single command.
	/// </summary>
	private sealed record AIMapCommand(
		string FilePath,
		int SpanStart,
		ImmutableArray<CommandModel> Commands,
		RegistryNodeSnapshot? TypeSnapshot = null,
		/// <summary>
		/// Diagnostics accumulated while expanding <see cref="TypeSnapshot"/> (e.g. AGH0007 duplicate CLI names,
		/// AGH0032 filesystem attribute misuse) — empty for the <c>Map(name, handler)</c> overload, which reports
		/// directly via its own <see cref="DiagnosticAccumulator"/> plumbing.
		/// </summary>
		ImmutableArray<PendingDiagnostic> EmbeddedDiagnostics = default)
		: AnalyzedInvocation(FilePath, SpanStart)
	{
		public ImmutableArray<PendingDiagnostic> EmbeddedDiagnosticsOrEmpty =>
			EmbeddedDiagnostics.IsDefault ? ImmutableArray<PendingDiagnostic>.Empty : EmbeddedDiagnostics;
	}

	/// <summary>An <c>AddRootCommand(handler)</c> or <c>AddNamespaceRootCommand(handler)</c> invocation.</summary>
	private sealed record AIMapRootCommand(string FilePath, int SpanStart, CommandModel Cmd, bool IsNamespaceRoot)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>A <c>MapAndRootAlias&lt;T&gt;()</c> invocation — registers all T methods as named commands and marks one as the root alias.</summary>
	private sealed record AIMapAndRootAlias(
		string FilePath,
		int SpanStart,
		RegistryNodeSnapshot TypeSnapshot,
		ImmutableArray<PendingDiagnostic> EmbeddedDiagnostics)
		: AnalyzedInvocation(FilePath, SpanStart);

	/// <summary>
	/// An <c>AddNamespace(…)</c> invocation.
	/// LambdaBodyStart/End are character offsets into FilePath used to identify child invocations positionally.
	/// </summary>
	private sealed record AIMapNamespace(
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
		string RemarksInnerXml,
		/// <summary>Alias target set by <c>MapAndRootAlias&lt;T&gt;</c> — a reference to a command already in <see cref="Commands"/>.</summary>
		CommandModel? AliasCommand = null);

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

}
