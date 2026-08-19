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
			case "UseGlobalOptions" when method.IsGenericMethod && method.TypeArguments.Length > 0:
			{
				if (method.TypeArguments[0] is not INamedTypeSymbol go || go.TypeKind == TypeKind.Error)
					return null;
				var model = BuildOptionsTypeModel(go, semanticModel.Compilation);
				if (model is null) return null;
				return new AIUseGlobalOptions(filePath, spanStart, model);
			}
			case "UseNamespaceOptions" when method.IsGenericMethod && method.TypeArguments.Length > 0:
			{
				if (method.TypeArguments[0] is not INamedTypeSymbol gt || gt.TypeKind == TypeKind.Error)
					return null;
				var model = BuildOptionsTypeModel(gt, semanticModel.Compilation);
				if (model is null) return null;
				return new AIUseNamespaceOptions(filePath, spanStart, model);
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
			case "Map" when method.IsGenericMethod && method.TypeArguments.Length > 0:
			{
				if (method.TypeArguments[0] is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error)
					return null;
				// Always hoist: merge the type's methods directly into the current scope (root or namespace).
				var acc = new DiagnosticAccumulator();
				var wrapper = new RegistryNode();
				ExpandTypeRegistrationAcc(acc, invocation.GetLocation(), named, ImmutableArray<string>.Empty, mergeOuterTypeSegment: true, wrapper, parseOpts, semanticModel.Compilation);
				var snap = BuildRegistryNodeSnapshot(wrapper);
				return new AIMapCommand(filePath, spanStart, ImmutableArray<CommandModel>.Empty, TypeSnapshot: snap, EmbeddedDiagnostics: acc.ToImmutable());
			}
			case "MapAndRootAlias" when method.IsGenericMethod && method.TypeArguments.Length > 0:
			{
				if (method.TypeArguments[0] is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error)
					return null;
				var acc = new DiagnosticAccumulator();
				var wrapper = new RegistryNode();
				AddMethodsFromTypeAccForAlias(acc, invocation.GetLocation(), named, ImmutableArray<string>.Empty, wrapper, parseOpts, semanticModel.Compilation);
				var snap = BuildRegistryNodeSnapshot(wrapper);
				return new AIMapAndRootAlias(filePath, spanStart, snap, acc.ToImmutable());
			}
			case "Map" when invocation.ArgumentList.Arguments.Count >= 2:
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
					return new AIMapCommand(filePath, spanStart, node.Commands.ToImmutableArray());
				}
				var handler = ResolveHandlerMethodForAnalyze(semanticModel, handlerExpr);
				if (handler is null) return null;
				var acc2 = new DiagnosticAccumulator();
				var cmd = CommandModel.FromMethod(commandName, handler, parseOpts, ImmutableArray<string>.Empty, acc2, invocation.GetLocation(), semanticModel.Compilation);
				return new AIMapCommand(filePath, spanStart, ImmutableArray.Create(cmd), EmbeddedDiagnostics: acc2.ToImmutable());
			}
			case "UseCliDescription":
			{
				if (invocation.ArgumentList.Arguments.Count < 1) return null;
				var descExpr = invocation.ArgumentList.Arguments[0].Expression;
				var desc = TryGetStringLiteral(descExpr) ?? "";
				return new AIUseCliDescription(filePath, spanStart, desc);
			}
			case "UseSchemaVersion":
			{
				if (invocation.ArgumentList.Arguments.Count < 1) return null;
				var verExpr = invocation.ArgumentList.Arguments[0].Expression;
				var ver = TryGetStringLiteral(verExpr);
				if (string.IsNullOrWhiteSpace(ver)) return null;
				return new AIUseSchemaVersion(filePath, spanStart, ver!);
			}
			case "DocumentEnvironmentVariables":
				return AnalyzeDocumentEnvironmentVariables(invocation, filePath, spanStart);
			case "MapRoot":
			{
				if (invocation.ArgumentList.Arguments.Count < 1) return null;
				var isNs = IsInvocationInsideMapNamespaceConfigure(invocation);
				return AnalyzeMapRootInvocation(invocation, semanticModel, filePath, spanStart, parseOpts, isNamespaceRoot: isNs);
			}
			case "MapNamespace":
				return AnalyzeMapNamespaceInvocation(invocation, semanticModel, filePath, spanStart, parseOpts, ct);
			default:
				return null;
		}
	}

	private static bool IsInvocationInsideMapNamespaceConfigure(InvocationExpressionSyntax invocation)
	{
		for (var n = invocation.Parent; n != null; n = n.Parent)
		{
			if (n is LambdaExpressionSyntax lambda && IsMapNamespaceConfigureLambda(lambda, out _))
				return true;
		}

		return false;
	}

	private static AIMapRootCommand? AnalyzeMapRootInvocation(
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
			return new AIMapRootCommand(filePath, spanStart, node.RootCommand, isNamespaceRoot);
		}
		var handler = ResolveHandlerMethodForAnalyze(semanticModel, handlerExpr);
		if (handler is null) return null;
		var acc = new DiagnosticAccumulator();
		var cmd = CommandModel.FromRootMethod(handler, parseOpts, ImmutableArray<string>.Empty, acc, invocation.GetLocation(), semanticModel.Compilation);
		return new AIMapRootCommand(filePath, spanStart, cmd, isNamespaceRoot);
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

	private static AIMapNamespace? AnalyzeMapNamespaceInvocation(
		InvocationExpressionSyntax invocation,
		SemanticModel semanticModel,
		string filePath,
		int spanStart,
		CSharpParseOptions parseOpts,
		CancellationToken ct)
	{
		if (invocation.ArgumentList.Arguments.Count < 1)
			return null;

		if (semanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol addNsMethod || addNsMethod.Name != "MapNamespace")
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
			var typeXml = namespaceEntryType.GetDocumentationCommentXml();
			if (string.IsNullOrWhiteSpace(typeXml))
				typeXml = TryExtractFullDocumentationFromTypeTrivia(namespaceEntryType);
			var (sx, rx) = Documentation.GetTypeDocumentation(typeXml);
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
			ExpandTypeRegistrationAcc(acc, invocation.GetLocation(), namespaceEntryType, ImmutableArray<string>.Empty, mergeOuterTypeSegment: true, entryNode, parseOpts, semanticModel.Compilation);
			entryTypeSnapshot = BuildRegistryNodeSnapshot(entryNode);
		}

		return new AIMapNamespace(
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
		CSharpParseOptions parseOpts,
		Compilation? compilation)
	{
		if (mergeOuterTypeSegment)
		{
			AddMethodsFromTypeAcc(acc, location, type, routePrefix, attachTo, parseOpts, compilation);
		}
		else
		{
			var seg = Naming.ToTypeSegmentName(type.Name);
			var wrapper = new RegistryNode();
			var outerPrefix = AppendSegment(routePrefix, seg);
			ExpandTypeRegistrationAcc(acc, location, type, outerPrefix, mergeOuterTypeSegment: true, wrapper, parseOpts, compilation);
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
			node.RemarksInnerXml,
			AliasCommand: node.RootAlias);
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
			if (ai is AIMapNamespace ns)
				foreach (var pd in ns.EmbeddedDiagnostics)
					context.ReportDiagnostic(Diagnostic.Create(GetDescriptorById(pd.DescriptorId), pd.Span.ToLocation(), pd.Arg0, pd.Arg1));
		}

		var sorted = allAnalyzed
			.OrderBy(a => a.FilePath, StringComparer.Ordinal)
			.ThenBy(a => a.SpanStart)
			.ToList();

		// Identify root-level invocations: those NOT contained inside any AIMapNamespace lambda body.
		var rootAnalyzed = new List<AnalyzedInvocation>();
		foreach (var ai in sorted)
		{
			if (!IsInsideAnyMapNamespaceLambda(ai, sorted))
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

		foreach (var ai in rootAnalyzed)
		{
			if (ai is AIUseCliDescription { Description: var desc } && !string.IsNullOrWhiteSpace(desc))
			{
				app.RootSummary = desc;
				break;
			}
		}

		foreach (var ai in rootAnalyzed)
		{
			if (ai is AIDocumentEnvironmentVariables { Variables: var vars, ConfigFiles: var cfgs })
			{
				if (!vars.IsDefaultOrEmpty) app.EnvironmentVars = vars;
				if (!cfgs.IsDefaultOrEmpty) app.ConfigFiles = cfgs;
				break;
			}
		}

		foreach (var ai in rootAnalyzed)
		{
			if (ai is AIUseSchemaVersion { Version: var v } && !string.IsNullOrWhiteSpace(v))
			{
				app.SchemaVersionOverride = v;
				break;
			}
		}

		ProcessAnalyzedInvocationsForNode(context, sorted, rootAnalyzed, app.Root, ImmutableArray<string>.Empty, app, isRoot: true);

		if (!string.IsNullOrWhiteSpace(app.RootSummary) && app.Root.RootCommand is not null)
		{
			var descAi = rootAnalyzed.OfType<AIUseCliDescription>().FirstOrDefault();
			var loc = descAi is not null
				? Location.Create(descAi.FilePath, new Microsoft.CodeAnalysis.Text.TextSpan(descAi.SpanStart, 0), default)
				: Location.None;
			context.ReportDiagnostic(Diagnostic.Create(UseCliDescriptionConflictsWithMapRoot, loc));
		}

		ValidateCommandNamespaceOptionsChain(context, app.Root, parentEffectiveOptionsMetadataName: app.GlobalOptionsModel?.TypeMetadataName);
		if (!ValidateNamespaceSegmentSanitizationCollisions(context, app.Root))
			return false;

		// OptionsModels are already set from AIUseGlobalOptions / AIUseNamespaceOptions via ProcessAnalyzedInvocationsForNode.

		var flat = new List<CommandModel>();
		CollectCommands(app.Root, flat);
		model = app;
		if (flat.Count == 0)
			return false;

		var dedup = new Dictionary<string, CommandModel>(StringComparer.OrdinalIgnoreCase);
		foreach (var c in flat)
		{
			var key = string.Join("/", c.RoutePrefix) + "/" + c.CommandName;
			if (dedup.ContainsKey(key))
			{
				context.ReportDiagnostic(Diagnostic.Create(DuplicateCommandName, c.HandlerSpanInfo.ToLocation(), c.CommandName));
				continue;
			}
			dedup[key] = c;
		}

		app.AllCommands = dedup.Values.ToImmutableArray();
		// GlobalOptionsModel is set during ProcessAnalyzedInvocationsForNode.

		// Pre-compute injection chains once per command; reused by validation, FixOptionsParamsInCommands, EmitOptionsReconstructLocals, and emit.
		// [NoOptionsInjection] only suppresses handler parameters and AGH0021 — globals/namespaced flags must still splice as OptionsInjected
		// for short/long parsing and static-field reconstruction after the route segment.
		app.InjectionChains = app.AllCommands.ToImmutableDictionary(
			cmd => cmd.RunMethodName,
			cmd => BuildOptionsInjectionChain(app, cmd),
			StringComparer.Ordinal);

		ValidateCommandOptionsInjection(context, app);
		FixOptionsParamsInCommands(app);
		ValidateDuplicateShortOptionLetters(context, app);

		return true;
	}

	/// <summary>Determines if a given AnalyzedInvocation is positionally inside any AIMapNamespace lambda body.</summary>
	private static bool IsInsideAnyMapNamespaceLambda(AnalyzedInvocation ai, List<AnalyzedInvocation> all)
	{
		foreach (var other in all)
		{
			if (other is not AIMapNamespace ns) continue;
			if (ns.LambdaBodyStart < 0 || ns.LambdaBodyEnd < 0) continue;
			if (!string.Equals(ns.FilePath, ai.FilePath, StringComparison.Ordinal)) continue;
			// Inclusive lower bound: for expression-bodied lambdas (e.g. g => g.MapNamespace(...)),
			// the nested invocation's SpanStart equals the lambda body's SpanStart and must count as inside.
			if (ai.SpanStart >= ns.LambdaBodyStart && ai.SpanStart < ns.LambdaBodyEnd)
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
				case AIUseGlobalOptions g when isRoot:
					app.GlobalOptionsModel = g.Model;
					break;
				case AIUseGlobalOptions when !isRoot:
					context.ReportDiagnostic(Diagnostic.Create(
						CommandNamespaceOptionsRequiresParent,
						Location.None,
						"T"));
					break;
				case AIUseNamespaceOptions ns when !isRoot:
					node.CommandNamespaceOptionsModel = ns.Model;
					node.CommandNamespaceOptionsLocation = Location.None;
					break;
				case AIUseNamespaceOptions when isRoot:
					context.ReportDiagnostic(Diagnostic.Create(
						CommandNamespaceOptionsRequiresParent,
						Location.None,
						"T"));
					break;
				case AIMapCommand { TypeSnapshot: { } typeSnap } mapCmd:
				{
					foreach (var pd in mapCmd.EmbeddedDiagnosticsOrEmpty)
						context.ReportDiagnostic(Diagnostic.Create(GetDescriptorById(pd.DescriptorId), pd.Span.ToLocation(), pd.Arg0, pd.Arg1));
					// Map<T> always hoists: merge the snapshot's commands directly into the current node.
					if (typeSnap.RootCommand is { } snapRc && node.RootCommand is not null)
						context.ReportDiagnostic(Diagnostic.Create(DuplicateRootCommand, snapRc.HandlerSpanInfo.ToLocation()));
					ApplyRegistryNodeSnapshot(typeSnap, node, currentPath);
					break;
				}
				case AIMapAndRootAlias alias:
				{
					foreach (var pd in alias.EmbeddedDiagnostics)
						context.ReportDiagnostic(Diagnostic.Create(GetDescriptorById(pd.DescriptorId), pd.Span.ToLocation(), pd.Arg0, pd.Arg1));
					if (node.RootAlias is not null || node.RootCommand is not null)
					{
						context.ReportDiagnostic(Diagnostic.Create(DuplicateRootCommand, Location.None));
						break;
					}
					ApplyRegistryNodeSnapshot(alias.TypeSnapshot, node, currentPath);
					break;
				}
				case AIMapCommand ac:
					foreach (var pd in ac.EmbeddedDiagnosticsOrEmpty)
						context.ReportDiagnostic(Diagnostic.Create(GetDescriptorById(pd.DescriptorId), pd.Span.ToLocation(), pd.Arg0, pd.Arg1));
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
				case AIMapRootCommand rc when isRoot && rc.IsNamespaceRoot:
					context.ReportDiagnostic(Diagnostic.Create(AddNamespaceRootCommandOnlyInNamespace, Location.None));
					break;
				case AIMapRootCommand rc when !isRoot && !rc.IsNamespaceRoot:
					context.ReportDiagnostic(Diagnostic.Create(AddRootCommandOnlyAtAppRoot, Location.None));
					break;
				case AIMapRootCommand rc:
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
				case AIMapNamespace ns:
					ProcessAnalyzedMapNamespace(context, allAnalyzed, ns, node, currentPath, app, isRoot);
					break;
			}
		}
	}

	private static void ProcessAnalyzedMapNamespace(
		SourceProductionContext context,
		List<AnalyzedInvocation> allAnalyzed,
		AIMapNamespace ns,
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
				if (other.SpanStart < ns.LambdaBodyStart || other.SpanStart >= ns.LambdaBodyEnd) continue;
				// Skip invocations that are nested inside a deeper lambda (not direct children).
				if (IsInsideAnyNestedMapNamespaceLambda(other, allAnalyzed, ns)) continue;
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
	private static bool IsInsideAnyNestedMapNamespaceLambda(AnalyzedInvocation ai, List<AnalyzedInvocation> all, AIMapNamespace parent)
	{
		foreach (var other in all)
		{
			if (other is not AIMapNamespace nested) continue;
			if (ReferenceEquals(nested, parent)) continue;
			if (nested.LambdaBodyStart < 0 || nested.LambdaBodyEnd < 0) continue;
			if (!string.Equals(nested.FilePath, ai.FilePath, StringComparison.Ordinal)) continue;
			// nested must itself be inside parent (inclusive lower bound for expression-bodied lambdas).
			if (nested.SpanStart < parent.LambdaBodyStart || nested.SpanStart >= parent.LambdaBodyEnd) continue;
			// ai must be inside nested
			if (ai.SpanStart >= nested.LambdaBodyStart && ai.SpanStart < nested.LambdaBodyEnd)
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
		CommandModel? prefixedAlias = null;
		foreach (var cmd in snap.Commands)
		{
			var prefixed = cmd with
			{
				RoutePrefix = path,
				RunMethodName = CommandModel.BuildRunMethodNameStatic(path, cmd.CommandName)
			};
			target.Commands.Add(prefixed);
			// Track the re-prefixed alias if this command was designated as the alias target.
			if (snap.AliasCommand is not null && cmd.CommandName == snap.AliasCommand.CommandName)
				prefixedAlias = prefixed;
		}
		if (prefixedAlias is not null)
			target.RootAlias ??= prefixedAlias;
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
				// mp.TypeMetadataName == requiredMetaName: exact match
				// mp.TypeAllBaseTypeMetadataNames.Contains(requiredMetaName): mp's type is a subclass of the required type
				if (mp.TypeMetadataName == requiredMetaName ||
				    mp.TypeAllBaseTypeMetadataNames.Contains(requiredMetaName))
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
			// Lambdas have no reconstructed options-instance surface; globals still participate via leading prefetch only when applicable.
			if (cmd.IsLambda)
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
				// Keep the param only if its type is NOT the options type and NOT a subclass of it.
				// handlerParam.TypeAllBaseTypeMetadataNames.Contains(o.TypeMetadataName) = param's type inherits from the options type.
				return !injChain.Any(o =>
					o.TypeMetadataName == handlerParam.TypeMetadataName ||
					handlerParam.TypeAllBaseTypeMetadataNames.Contains(o.TypeMetadataName));
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
		if (node.RootAlias is not null && fixedById.TryGetValue(node.RootAlias.RunMethodName, out var fixedAlias))
			node.RootAlias = fixedAlias;
		// node.Commands is read by schema emission — fix it too so injected options params are stripped from schema output
		for (var i = 0; i < node.Commands.Count; i++)
		{
			if (fixedById.TryGetValue(node.Commands[i].RunMethodName, out var fixedCmd))
				node.Commands[i] = fixedCmd;
		}
		foreach (var child in node.Children)
			UpdateRegistryNodeRootCommands(child.Node, fixedById);
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



	private static bool IsMapNamespaceConfigureLambda(LambdaExpressionSyntax lambda, out InvocationExpressionSyntax addNamespaceInv)
	{
		addNamespaceInv = null!;
		if (lambda.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax al })
			return false;
		if (al.Parent is not InvocationExpressionSyntax inv)
			return false;
		if (inv.Expression is not MemberAccessExpressionSyntax ma || ma.Name is not SimpleNameSyntax sns ||
		    sns.Identifier.Text != "MapNamespace")
			return false;
		var last = al.Arguments.Count - 1;
		if (last < 0 || !ReferenceEquals(al.Arguments[last].Expression, lambda))
			return false;
		addNamespaceInv = inv;
		return true;
	}


	/// <summary>
	/// Synthesizes the fully-qualified BCL delegate type (<c>System.Func&lt;...&gt;</c> / <c>System.Action&lt;...&gt;</c>)
	/// that the C# compiler infers as the "natural type" for a lambda with this signature. Used instead of reading the
	/// converted-to type off the enclosing <see cref="IConversionOperation"/>, because when a lambda is passed to a
	/// <c>Delegate</c>-typed parameter (e.g. <c>Map(string, Delegate)</c>) that conversion's <c>Type</c> is
	/// <c>System.Delegate</c> itself, not the lambda's actual runtime delegate type.
	/// </summary>
	private static string? BuildNaturalDelegateTypeFq(IMethodSymbol invokeMethod)
	{
		if (invokeMethod.Parameters.Length > 16)
			return null; // Func<>/Action<> top out at 16 parameters; fall back to Delegate.

		foreach (var p in invokeMethod.Parameters)
			if (p.RefKind != RefKind.None)
				return null; // ref/out/in params have no Func<>/Action<> natural type; fall back to Delegate.

		var paramFqs = new string[invokeMethod.Parameters.Length];
		for (var i = 0; i < invokeMethod.Parameters.Length; i++)
			paramFqs[i] = invokeMethod.Parameters[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		if (invokeMethod.ReturnsVoid)
		{
			return paramFqs.Length == 0
				? "global::System.Action"
				: $"global::System.Action<{string.Join(", ", paramFqs)}>";
		}

		var retFq = invokeMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var allArgs = paramFqs.Length == 0 ? retFq : string.Join(", ", paramFqs) + ", " + retFq;
		return $"global::System.Func<{allArgs}>";
	}

	/// <summary>Select-step (no SourceProductionContext) variant of <see cref="TryExpandLambdaDelegate"/>.</summary>
	private static void TryExpandLambdaDelegateAcc(
		SemanticModel model,
		InvocationExpressionSyntax invocation,
		ExpressionSyntax handlerExpr,
		string commandName,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode) =>
		TryExpandLambdaDelegate(null, model, invocation, handlerExpr, commandName, routePrefix, targetNode);

	private static void TryExpandLambdaDelegate(
		SourceProductionContext? context,
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

		if (op is IAnonymousFunctionOperation anonFunc)
			invokeMethod = anonFunc.Symbol;

		if (invokeMethod is null)
			return;

		// Build the storage key: "namespace/name" for nested, "name" for root
		var storageKey = routePrefix.IsDefaultOrEmpty
			? commandName
			: string.Join("/", routePrefix) + "/" + commandName;

		// Get the FQ delegate type string for casting at runtime. Synthesized from the lambda's own signature
		// rather than the enclosing conversion's Type, which — since the target parameter is `Delegate` — would
		// otherwise resolve to `System.Delegate` itself and force a reflection-based DynamicInvoke fallback that
		// silently discards the handler's return value (see BuildNaturalDelegateTypeFq).
		var delegateFq = BuildNaturalDelegateTypeFq(invokeMethod) ?? "global::System.Delegate";

		var parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;

		// Build parameter models from the lambda's method symbol
		var paramBuilder = ImmutableArray.CreateBuilder<ParameterModel>();
		foreach (var p in invokeMethod.Parameters)
		{
			paramBuilder.Add(ParameterModel.From(p, reportFallbackLocation: invocation.GetLocation()));
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
		// FullyQualifiedFormat renders special types via their C# keyword ("void"), not "global::System.Void".
		var retIsVoid = retFq is "void"
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


	/// <summary>Select-step (no SourceProductionContext) variant of <see cref="TryExpandLambdaRootCommand"/>.</summary>
	private static void TryExpandLambdaRootCommandAcc(
		SemanticModel model,
		InvocationExpressionSyntax invocation,
		ExpressionSyntax handlerExpr,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode) =>
		TryExpandLambdaRootCommand(null, model, invocation, handlerExpr, routePrefix, targetNode);

	private static void TryExpandLambdaRootCommand(
		SourceProductionContext? context,
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

		if (invokeMethod is null)
			return;

		var storageKey = routePrefix.IsDefaultOrEmpty
			? "__argh_root"
			: string.Join("/", routePrefix) + "/__argh_root";
		// Synthesized from the lambda's own signature — see BuildNaturalDelegateTypeFq for why the enclosing
		// conversion's Type (System.Delegate) can't be used here.
		var delegateFq = BuildNaturalDelegateTypeFq(invokeMethod) ?? "global::System.Delegate";
		var parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;
		var paramBuilder = ImmutableArray.CreateBuilder<ParameterModel>();
		foreach (var p in invokeMethod.Parameters)
			paramBuilder.Add(ParameterModel.From(p, reportFallbackLocation: invocation.GetLocation()));
		var parameters = paramBuilder.ToImmutable();
		var usage = UsageSynopsis.Build(parameters);
		var runName = CommandModel.BuildRootDefaultRunMethodName(routePrefix);
		var lambdaRetFq = invokeMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		// FullyQualifiedFormat renders special types via their C# keyword ("void"), not "global::System.Void".
		var lambdaRetIsVoid = lambdaRetFq is "void"
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


	private static ImmutableArray<string> AppendSegment(ImmutableArray<string> prefix, string segment)
	{
		var b = ImmutableArray.CreateBuilder<string>(prefix.Length + 1);
		foreach (var s in prefix)
			b.Add(s);
		b.Add(segment);
		return b.MoveToImmutable();
	}


	/// <summary>DiagnosticAccumulator-based variant of <see cref="AddMethodsFromType"/> for use in the Select-step analysis.</summary>
	private static void AddMethodsFromTypeAcc(
		DiagnosticAccumulator acc,
		Location location,
		INamedTypeSymbol type,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode,
		CSharpParseOptions parseOpts,
		Compilation? compilation)
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
				targetNode.RootCommand = CommandModel.FromRootMethod(defaultCommand, parseOpts, routePrefix, acc, location, compilation);
		}
		foreach (var member in type.GetMembers())
		{
			if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary) continue;
			if (method.AssociatedSymbol is not null) continue;
			if (method.DeclaredAccessibility != Accessibility.Public) continue;
			if (defaultCommand is not null && SymbolEqualityComparer.Default.Equals(method, defaultCommand)) continue;
			var cmdName = TryGetCommandNameAttribute(method) ?? Naming.ToCommandName(method.Name);
			targetNode.Commands.Add(CommandModel.FromMethod(cmdName, method, parseOpts, routePrefix, acc, location, compilation));
		}
	}

	/// <summary>
	/// Variant of <see cref="AddMethodsFromTypeAcc"/> used by <c>MapAndRootAlias&lt;T&gt;</c>.
	/// All public methods are registered as regular named commands (none extracted to RootCommand).
	/// The [DefaultCommand]-marked method (or the sole method for single-method types) is also stored
	/// in <see cref="RegistryNode.RootAlias"/> as the alias target.
	/// </summary>
	private static void AddMethodsFromTypeAccForAlias(
		DiagnosticAccumulator acc,
		Location location,
		INamedTypeSymbol type,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode,
		CSharpParseOptions parseOpts,
		Compilation? compilation)
	{
		IMethodSymbol? defaultCommandMethod = null;
		var publicOrdinaryMethods = new List<IMethodSymbol>();

		foreach (var member in type.GetMembers())
		{
			if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary) continue;
			if (method.AssociatedSymbol is not null) continue;
			if (method.DeclaredAccessibility != Accessibility.Public) continue;
			publicOrdinaryMethods.Add(method);
			if (!HasDefaultCommandAttribute(method)) continue;
			if (defaultCommandMethod is not null)
			{
				acc.Add(MultipleDefaultCommandAttributes, method.Locations.FirstOrDefault() ?? location, type.Name);
				continue;
			}
			defaultCommandMethod = method;
		}

		// Auto-select for single-method types; require [DefaultCommand] for multi-method types.
		if (defaultCommandMethod is null)
		{
			if (publicOrdinaryMethods.Count == 1)
				defaultCommandMethod = publicOrdinaryMethods[0];
			else if (publicOrdinaryMethods.Count > 1)
				acc.Add(MapAndRootAliasAmbiguousTarget, location, type.Name);
		}

		foreach (var method in publicOrdinaryMethods)
		{
			var cmdName = TryGetCommandNameAttribute(method) ?? Naming.ToCommandName(method.Name);
			var cmd = CommandModel.FromMethod(cmdName, method, parseOpts, routePrefix, acc, location, compilation);
			targetNode.Commands.Add(cmd);
			if (defaultCommandMethod is not null && SymbolEqualityComparer.Default.Equals(method, defaultCommandMethod))
				targetNode.RootAlias = cmd;
		}
	}

}
