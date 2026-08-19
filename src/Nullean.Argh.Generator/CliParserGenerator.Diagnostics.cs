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
	private static readonly DiagnosticDescriptor CommandNamespaceOptionsMustExtendParent = new(
		"AGH0004",
		"Command namespace options type must extend the parent options type",
		"'{0}' must inherit or implement '{1}' for this UseNamespaceOptions<> registration.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor CommandNamespaceOptionsRequiresParent = new(
		"AGH0005",
		"Command namespace options require a parent options type",
		"Register UseGlobalOptions<T>() before UseNamespaceOptions<{0}>(), or ensure the parent namespace declares a compatible base options type.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor HandlerMustBeMethod = new(
		"AGH0002",
		"Command handler must be a method group",
		"The second argument to Map must be a method group (not a lambda or local function) so the generator can emit an AOT-compatible call.",
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
		"Collection types are only supported for option flags, not for [Argument] positionals. Use a T[] type for a variadic positional.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor VariadicMustBeLastPositional = new(
		"AGH0031",
		"Variadic positional must be last",
		"A variadic positional (T[] with [Argument]) must be the last positional parameter; no [Argument] parameter may follow it.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor VariadicCollectionMustBeArray = new(
		"AGH0034",
		"Variadic positional must be a T[] array",
		"A variadic positional ([Argument] on a collection) must be declared as a T[] array type. List<T> and other collection interfaces are not supported.",
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
		"Only one default handler per scope: MapRoot, or [DefaultCommand].",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor AddRootCommandOnlyAtAppRoot = new(
		"AGH0011",
		"MapRoot only on the root app",
		"Use MapRoot on the root ArghApp only (not inside MapNamespace). For a namespace default handler, call MapRoot inside the MapNamespace configure callback.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor AddNamespaceRootCommandOnlyInNamespace = new(
		"AGH0012",
		"MapRoot only inside a namespace",
		"Use MapRoot inside MapNamespace configuration. For the top-level default, use MapRoot at the app root.",
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
		"MapNamespace requires a description or entry type",
		"Use MapNamespace(string name, string description, Action<IArghBuilder> configure) with an explicit description (may be empty), or MapNamespace<T>(string name, Action<IArghBuilder> configure) to use type T's XML summary for the namespace listing.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor AddNamespaceDescriptionNotConstant = new(
		"AGH0015",
		"MapNamespace description not a compile-time string",
		"The description argument must be a string literal or const string so the generator can emit namespace help text.",
		"Argh",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor RedundantAddInsideAddNamespaceT = new(
		"AGH0016",
		"Redundant Map<T> inside MapNamespace<T>",
		"MapNamespace<{0}> already registers public commands from that type; remove the inner Map<{0}> call.",
		"Argh",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor NamespaceSegmentUnresolved = new(
		"AGH0017",
		"Namespace segment could not be resolved",
		"MapNamespace<{0}>() without a name requires [NamespaceSegment] with a string argument on the type and/or a single <c>segment</c> in the type XML <summary>.",
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
		"This MapNamespace block does not register any commands, nested namespaces, or default handlers.",
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

	private static readonly DiagnosticDescriptor UseCliDescriptionConflictsWithMapRoot = new(
		"AGH0023",
		"UseCliDescription conflicts with MapRoot",
		"UseCliDescription cannot be combined with MapRoot: the root command handler's XML summary is already shown as the description. Remove one or the other.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor UriSchemeOnNonUriParam = new(
		"AGH0024",
		"[UriScheme] applied to non-Uri parameter",
		"'{0}' has [UriScheme] but its type is not Uri or Uri?; [UriScheme] only constrains Uri-typed parameters.",
		"Argh",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor TimeSpanRangeOnNonTimeSpanParam = new(
		"AGH0025",
		"[TimeSpanRange] applied to non-TimeSpan parameter",
		"'{0}' has [TimeSpanRange] but its type is not TimeSpan or TimeSpan?; [TimeSpanRange] only constrains TimeSpan-typed parameters.",
		"Argh",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor BoolFlagCollidesWithNullableNegation = new(
		"AGH0026",
		"Bool flag collides with nullable bool negation",
		"Parameter '{0}' maps to '--{1}', which duplicates the negation flag generated for a nullable bool on the same command. Rename one of the parameters.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor DuplicateCommandName = new(
		"AGH0027",
		"Duplicate command name in scope",
		"The command name '{0}' is registered more than once in the same scope. Only the first registration is used; rename one command or use [CommandName] to assign a unique name.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor ReadOnlySetInvalidElementType = new(
		"AGH0028",
		"IReadOnlySet<T> element type is not supported",
		"IReadOnlySet<T> only supports value-type or enum element types; '{0}' is not allowed",
		"Usage",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor MapAndRootAliasAmbiguousTarget = new(
		"AGH0029",
		"MapAndRootAlias<T> requires a [DefaultCommand] target",
		"MapAndRootAlias<{0}> exposes multiple commands but none is marked [DefaultCommand]. Annotate exactly one method with [DefaultCommand] to designate the root alias target.",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor PathExistenceAttributesConflict = new(
		"AGH0030",
		"[Existing] and [NonExisting] conflict",
		"Parameter '{0}' cannot declare both [Existing] and [NonExisting].",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor FilesystemPathAttributeTypeMismatch = new(
		"AGH0032",
		"Filesystem path attribute incompatible with parameter type",
		"'{0}': {1}",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor DuplicateShortOption = new(
		"AGH0033",
		"Duplicate short option letter",
		"The short option '-{0}' is used for more than one flag in the same parse scope: '--{1}' and '--{2}' ({3}).",
		"Argh",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

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
		"AGH0023" => UseCliDescriptionConflictsWithMapRoot,
		"AGH0024" => UriSchemeOnNonUriParam,
		"AGH0025" => TimeSpanRangeOnNonTimeSpanParam,
		"AGH0026" => BoolFlagCollidesWithNullableNegation,
		"AGH0027" => DuplicateCommandName,
		"AGH0028" => ReadOnlySetInvalidElementType,
		"AGH0029" => MapAndRootAliasAmbiguousTarget,
		"AGH0030" => PathExistenceAttributesConflict,
		"AGH0032" => FilesystemPathAttributeTypeMismatch,
		"AGH0033" => DuplicateShortOption,
		_ => throw new ArgumentException($"Unknown diagnostic id: {id}")
	};

	/// <summary>
	/// Per-invocation semantic analysis, intended for the CreateSyntaxProvider Select step.
	/// Runs with a <see cref="SemanticModel"/> but produces a fully symbol-free <see cref="AnalyzedInvocation"/>
	/// so the pipeline boundary data is stable across unrelated edits.
	/// Diagnostics that cannot be reported here (no SourceProductionContext in Select step) are embedded
	/// in the returned record via EmbeddedDiagnostics and reported later by TryBuildAppEmitModel.
	/// </summary>
}
