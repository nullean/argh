using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Nullean.Argh;

[Generator]
public sealed class CliParserGenerator : IIncrementalGenerator
{
	private const string ArghAppMetadataName = "Nullean.Argh.ArghApp";
	private const string IArghBuilderMetadataName = "Nullean.Argh.IArghBuilder";
	private const string ArghBuilderMetadataName = "Nullean.Argh.ArghBuilder";

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

	private static readonly DiagnosticDescriptor UseFilterDelegateNotSupported = new(
		"AGH0006",
		"Inline UseFilter delegate not emitted",
		"UseFilter requires a type argument (UseFilter<T>()) for source-generated filters; inline delegates are not emitted.",
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

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValuesProvider<InvocationExpressionSyntax> invocations = context.SyntaxProvider
			.CreateSyntaxProvider(
				static (node, _) => node is InvocationExpressionSyntax
				{
					Expression: MemberAccessExpressionSyntax { Name: SimpleNameSyntax name }
				} && name.Identifier.Text is "Add" or "AddNamespace" or "GlobalOptions" or "CommandNamespaceOptions" or "UseFilter",
				static (ctx, _) => (InvocationExpressionSyntax)ctx.Node);

		IncrementalValueProvider<(Compilation Compilation, ImmutableArray<InvocationExpressionSyntax> Invocations)> combined =
			context.CompilationProvider.Combine(invocations.Collect());

		context.RegisterSourceOutput(combined, static (spc, tuple) => Execute(spc, tuple.Compilation, tuple.Invocations));
	}

	private static void GetCompilationAssemblyMetadata(Compilation compilation, out string assemblyName, out string assemblyVersion)
	{
		assemblyName = compilation.Assembly.Identity.Name ?? "app";
		assemblyVersion = compilation.Assembly.Identity.Version?.ToString() ?? "0.0.0.0";
	}

	private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<InvocationExpressionSyntax> invocations)
	{
		GetCompilationAssemblyMetadata(compilation, out string entryAsmName, out string entryAsmVersion);

		if (invocations.IsDefaultOrEmpty)
		{
			EmitEmpty(context, entryAsmName, entryAsmVersion);
			return;
		}

		INamedTypeSymbol? arghApp = compilation.GetTypeByMetadataName(ArghAppMetadataName);
		if (arghApp is null)
		{
			EmitEmpty(context, entryAsmName, entryAsmVersion);
			return;
		}

		INamedTypeSymbol? iArghBuilder = compilation.GetTypeByMetadataName(IArghBuilderMetadataName);
		INamedTypeSymbol? arghBuilderType = compilation.GetTypeByMetadataName(ArghBuilderMetadataName);

		var filtered = new List<InvocationExpressionSyntax>();
		foreach (InvocationExpressionSyntax invocation in invocations)
		{
			SemanticModel model = compilation.GetSemanticModel(invocation.SyntaxTree);
			if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
				continue;

			ITypeSymbol? receiver = GetReceiverType(model, invocation);
			if (receiver is null || !IsArghRegistrationReceiver(receiver, arghApp, iArghBuilder, arghBuilderType))
				continue;

			if (method.Name is not ("Add" or "AddNamespace" or "GlobalOptions" or "CommandNamespaceOptions" or "UseFilter"))
				continue;

			filtered.Add(invocation);
		}

		if (filtered.Count == 0)
		{
			EmitEmpty(context, entryAsmName, entryAsmVersion);
			return;
		}

		filtered.Sort(static (a, b) =>
		{
			FileLinePositionSpan la = a.SyntaxTree.GetLineSpan(a.Span);
			FileLinePositionSpan lb = b.SyntaxTree.GetLineSpan(b.Span);
			int c = string.CompareOrdinal(la.Path, lb.Path);
			if (c != 0)
				return c;
			return a.Span.Start.CompareTo(b.Span.Start);
		});

		if (!TryBuildAppEmitModel(context, compilation, filtered, out AppEmitModel? appModel) || appModel is null)
		{
			EmitEmpty(context, entryAsmName, entryAsmVersion);
			return;
		}

		CSharpParseOptions parseOpts = CSharpParseOptions.Default;
		foreach (SyntaxTree st in compilation.SyntaxTrees)
		{
			if (st.Options is CSharpParseOptions po)
			{
				parseOpts = po;
				break;
			}
		}

		EmitApp(context, appModel, parseOpts, entryAsmName, entryAsmVersion);
	}

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
		INamedTypeSymbol? arghBuilderType)
	{
		if (SymbolEqualityComparer.Default.Equals(receiver, arghApp))
			return true;

		if (iArghBuilder is not null && SymbolEqualityComparer.Default.Equals(receiver, iArghBuilder))
			return true;

		if (arghBuilderType is not null && SymbolEqualityComparer.Default.Equals(receiver, arghBuilderType))
			return true;

		if (iArghBuilder is not null && receiver is INamedTypeSymbol named)
		{
			foreach (INamedTypeSymbol iface in named.AllInterfaces)
			{
				if (SymbolEqualityComparer.Default.Equals(iface, iArghBuilder))
					return true;
			}
		}

		return false;
	}

	private sealed class RegistryNode
	{
		public readonly List<CommandModel> Commands = new();
		public readonly List<NamedCommandNamespaceChild> Children = new();
		public INamedTypeSymbol? CommandNamespaceOptionsType;
		public Location? CommandNamespaceOptionsLocation;
		public OptionsTypeModel? CommandNamespaceOptionsModel;

		public sealed class NamedCommandNamespaceChild
		{
			public string Segment = "";
			public RegistryNode Node = null!;
		}
	}

	private sealed class AppEmitModel
	{
		public INamedTypeSymbol? GlobalOptionsType;
		public OptionsTypeModel? GlobalOptionsModel;
		public readonly RegistryNode Root = new();
		public ImmutableArray<CommandModel> AllCommands = ImmutableArray<CommandModel>.Empty;
		public ImmutableArray<GlobalFilterRegistration> GlobalFilters = ImmutableArray<GlobalFilterRegistration>.Empty;
	}

	private sealed record GlobalFilterRegistration(INamedTypeSymbol FilterType);

	private sealed record OptionsTypeModel(INamedTypeSymbol Type, ImmutableArray<ParameterModel> Members);

	private static bool TryBuildAppEmitModel(
		SourceProductionContext context,
		Compilation compilation,
		List<InvocationExpressionSyntax> allInvocations,
		out AppEmitModel? model)
	{
		model = null;
		var rootInvocations = new List<InvocationExpressionSyntax>();
		foreach (InvocationExpressionSyntax inv in allInvocations)
		{
			if (FindParentAddNamespaceInvocation(inv) is null)
				rootInvocations.Add(inv);
		}

		var app = new AppEmitModel();
		CollectGlobalFilters(context, compilation, rootInvocations, out ImmutableArray<GlobalFilterRegistration> globalFilters);
		app.GlobalFilters = globalFilters;

		ProcessInvocationsForNode(context, compilation, allInvocations, rootInvocations, app.Root, ImmutableArray<string>.Empty, app,
			isRoot: true);

		ValidateCommandNamespaceOptionsChain(context, app.Root, parentEffectiveOptions: app.GlobalOptionsType);

		AttachCommandNamespaceOptionsModels(app.Root, context);

		var flat = new List<CommandModel>();
		CollectCommands(app.Root, flat);
		if (flat.Count == 0)
			return false;

		var dedup = new Dictionary<string, CommandModel>(StringComparer.OrdinalIgnoreCase);
		foreach (CommandModel c in flat)
		{
			var key = string.Join("/", c.RoutePrefix) + "/" + c.CommandName;
			dedup[key] = c;
		}

		app.AllCommands = dedup.Values.ToImmutableArray();
		if (app.GlobalOptionsType is not null)
			app.GlobalOptionsModel = BuildOptionsTypeModel(context, app.GlobalOptionsType);

		model = app;
		return true;
	}

	private static void AttachCommandNamespaceOptionsModels(RegistryNode node, SourceProductionContext context)
	{
		if (node.CommandNamespaceOptionsType is not null)
			node.CommandNamespaceOptionsModel = BuildOptionsTypeModel(context, node.CommandNamespaceOptionsType);

		foreach (RegistryNode.NamedCommandNamespaceChild ch in node.Children)
			AttachCommandNamespaceOptionsModels(ch.Node, context);
	}

	private static void ValidateCommandNamespaceOptionsChain(
		SourceProductionContext context,
		RegistryNode node,
		INamedTypeSymbol? parentEffectiveOptions)
	{
		if (node.CommandNamespaceOptionsType is not null)
		{
			if (parentEffectiveOptions is null)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					CommandNamespaceOptionsRequiresParent,
					node.CommandNamespaceOptionsLocation ?? Location.None,
					node.CommandNamespaceOptionsType.Name));
			}
			else if (!TypeInheritsFromOrImplements(node.CommandNamespaceOptionsType, parentEffectiveOptions))
			{
				context.ReportDiagnostic(Diagnostic.Create(
					CommandNamespaceOptionsMustExtendParent,
					node.CommandNamespaceOptionsLocation ?? Location.None,
					node.CommandNamespaceOptionsType.Name,
					parentEffectiveOptions.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)));
			}
		}

		INamedTypeSymbol? nextParent = node.CommandNamespaceOptionsType ?? parentEffectiveOptions;
		foreach (RegistryNode.NamedCommandNamespaceChild child in node.Children)
			ValidateCommandNamespaceOptionsChain(context, child.Node, nextParent);
	}

	private static bool TypeInheritsFromOrImplements(INamedTypeSymbol type, INamedTypeSymbol baseOrInterface)
	{
		INamedTypeSymbol? current = type;
		while (current is not null)
		{
			if (SymbolEqualityComparer.Default.Equals(current, baseOrInterface))
				return true;
			current = current.BaseType;
		}

		foreach (INamedTypeSymbol iface in type.AllInterfaces)
		{
			if (SymbolEqualityComparer.Default.Equals(iface, baseOrInterface))
				return true;
		}

		return false;
	}

	private static void CollectCommands(RegistryNode node, List<CommandModel> sink)
	{
		sink.AddRange(node.Commands);
		foreach (RegistryNode.NamedCommandNamespaceChild child in node.Children)
			CollectCommands(child.Node, sink);
	}

	private static void ProcessInvocationsForNode(
		SourceProductionContext context,
		Compilation compilation,
		List<InvocationExpressionSyntax> allInvocations,
		List<InvocationExpressionSyntax> nodeInvocations,
		RegistryNode node,
		ImmutableArray<string> currentPath,
		AppEmitModel app,
		bool isRoot)
	{
		SemanticModel GetModel(InvocationExpressionSyntax inv) => compilation.GetSemanticModel(inv.SyntaxTree);

		foreach (InvocationExpressionSyntax invocation in nodeInvocations)
		{
			if (GetModel(invocation).GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
				continue;

			switch (method.Name)
			{
				case "GlobalOptions" when isRoot && method.IsGenericMethod && method.TypeArguments.Length > 0:
				{
					if (method.TypeArguments[0] is INamedTypeSymbol go && go.TypeKind != TypeKind.Error)
					{
						app.GlobalOptionsType = go;
					}

					break;
				}
				case "GlobalOptions" when !isRoot:
					context.ReportDiagnostic(Diagnostic.Create(
						CommandNamespaceOptionsRequiresParent,
						invocation.GetLocation(),
						method.TypeArguments.Length > 0 ? method.TypeArguments[0].Name : "T"));
					break;
				case "CommandNamespaceOptions" when isRoot:
					context.ReportDiagnostic(Diagnostic.Create(
						CommandNamespaceOptionsRequiresParent,
						invocation.GetLocation(),
						method is { IsGenericMethod: true, TypeArguments.Length: > 0 }
							? method.TypeArguments[0].Name
							: "T"));
					break;
				case "CommandNamespaceOptions" when method.IsGenericMethod && method.TypeArguments.Length > 0:
				{
					if (method.TypeArguments[0] is INamedTypeSymbol gt && gt.TypeKind != TypeKind.Error)
					{
						node.CommandNamespaceOptionsType = gt;
						node.CommandNamespaceOptionsLocation = invocation.GetLocation();
					}

					break;
				}
				case "AddNamespace":
					ProcessAddNamespaceInvocation(context, compilation, allInvocations, invocation, node, currentPath, app);
					break;
				case "Add" when method.IsGenericMethod:
				{
					ITypeSymbol? typeArg = method.TypeArguments.Length > 0 ? method.TypeArguments[0] : null;
					if (typeArg is INamedTypeSymbol named && typeArg.TypeKind != TypeKind.Error)
					{
						CSharpParseOptions parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;
						ExpandTypeRegistration(context, invocation, named, currentPath, mergeOuterTypeSegment: !isRoot, node,
							parseOpts);
					}

					break;
				}
				case "Add":
				{
					if (invocation.ArgumentList.Arguments.Count < 2)
						continue;
					ExpandAddStringDelegate(context, GetModel(invocation), invocation, currentPath, node);
					break;
				}
			}
		}
	}

	private static void ProcessAddNamespaceInvocation(
		SourceProductionContext context,
		Compilation compilation,
		List<InvocationExpressionSyntax> allInvocations,
		InvocationExpressionSyntax addNamespaceInvocation,
		RegistryNode parentNode,
		ImmutableArray<string> parentPath,
		AppEmitModel app)
	{
		if (addNamespaceInvocation.ArgumentList.Arguments.Count < 2)
			return;

		string? segmentName = TryGetStringLiteral(addNamespaceInvocation.ArgumentList.Arguments[0].Expression);
		if (string.IsNullOrWhiteSpace(segmentName))
			return;

		var childNode = new RegistryNode();
		var childInvocations = new List<InvocationExpressionSyntax>();
		foreach (InvocationExpressionSyntax inv in allInvocations)
		{
			if (FindParentAddNamespaceInvocation(inv) is { } p && ReferenceEquals(p, addNamespaceInvocation))
				childInvocations.Add(inv);
		}

		childInvocations.Sort(static (a, b) =>
		{
			FileLinePositionSpan la = a.SyntaxTree.GetLineSpan(a.Span);
			FileLinePositionSpan lb = b.SyntaxTree.GetLineSpan(b.Span);
			int c = string.CompareOrdinal(la.Path, lb.Path);
			if (c != 0)
				return c;
			return a.Span.Start.CompareTo(b.Span.Start);
		});

		ImmutableArray<string> childPath = AppendSegment(parentPath, segmentName!);
		ProcessInvocationsForNode(context, compilation, allInvocations, childInvocations, childNode, childPath, app,
			isRoot: false);
		parentNode.Children.Add(new RegistryNode.NamedCommandNamespaceChild { Segment = segmentName!, Node = childNode });
	}

	private static InvocationExpressionSyntax? FindParentAddNamespaceInvocation(InvocationExpressionSyntax invocation)
	{
		for (SyntaxNode? n = invocation.Parent; n != null; n = n.Parent)
		{
			if (n is LambdaExpressionSyntax lambda && IsSecondArgOfAddNamespaceLambda(lambda, out InvocationExpressionSyntax addNamespaceInv))
				return addNamespaceInv;
		}

		return null;
	}

	private static bool IsSecondArgOfAddNamespaceLambda(LambdaExpressionSyntax lambda, out InvocationExpressionSyntax addNamespaceInv)
	{
		addNamespaceInv = null!;
		if (lambda.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax al })
			return false;
		if (al.Parent is not InvocationExpressionSyntax inv)
			return false;
		if (inv.Expression is not MemberAccessExpressionSyntax ma || ma.Name.Identifier.Text != "AddNamespace")
			return false;
		if (al.Arguments.Count < 2 || !ReferenceEquals(al.Arguments[1].Expression, lambda))
			return false;
		addNamespaceInv = inv;
		return true;
	}

	private static void ExpandAddStringDelegate(
		SourceProductionContext context,
		SemanticModel model,
		InvocationExpressionSyntax invocation,
		ImmutableArray<string> routePrefix,
		RegistryNode targetNode)
	{
		ExpressionSyntax nameExpr = invocation.ArgumentList.Arguments[0].Expression;
		ExpressionSyntax handlerExpr = invocation.ArgumentList.Arguments[1].Expression;

		string? commandName = TryGetStringLiteral(nameExpr);
		if (commandName is null || string.IsNullOrWhiteSpace(commandName))
			return;

		// Detect lambda expressions — handle them as stored-delegate commands
		if (handlerExpr is LambdaExpressionSyntax)
		{
			TryExpandLambdaDelegate(context, model, invocation, handlerExpr, commandName, routePrefix, targetNode);
			return;
		}

		IMethodSymbol? handler = ResolveHandlerMethod(model, handlerExpr, context, invocation);
		if (handler is null)
			return;

		CSharpParseOptions parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;
		targetNode.Commands.Add(CommandModel.FromMethod(commandName, handler, parseOpts, routePrefix, context, invocation.GetLocation()));
	}

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
		IOperation? op = model.GetOperation(handlerExpr);
		// Unwrap conversions
		while (op is IConversionOperation conv)
			op = conv.Operand;

		IMethodSymbol? invokeMethod = null;
		INamedTypeSymbol? delegateType = null;

		if (op is IAnonymousFunctionOperation anonFunc)
		{
			invokeMethod = anonFunc.Symbol;
			// Get the converted-to delegate type from the parent conversion
			IOperation? parent = model.GetOperation(handlerExpr);
			if (parent is IConversionOperation parentConv && parentConv.Type is INamedTypeSymbol dt)
				delegateType = dt;
		}

		if (invokeMethod is null)
			return;

		// Build the storage key: "namespace/name" for nested, "name" for root
		string storageKey = routePrefix.IsDefaultOrEmpty
			? commandName
			: string.Join("/", routePrefix) + "/" + commandName;

		// Get the FQ delegate type string for casting at runtime
		string delegateFq = delegateType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Delegate";

		CSharpParseOptions parseOpts = invocation.SyntaxTree.Options as CSharpParseOptions ?? CSharpParseOptions.Default;

		// Build parameter models from the lambda's method symbol
		ImmutableArray<ParameterModel>.Builder paramBuilder = ImmutableArray.CreateBuilder<ParameterModel>();
		foreach (IParameterSymbol p in invokeMethod.Parameters)
		{
			paramBuilder.Add(ParameterModel.From(p));
		}
		ImmutableArray<ParameterModel> parameters = paramBuilder.ToImmutable();
		string usage = UsageSynopsis.Build(parameters);
		// Build run method name inline (mirrors CommandModel.BuildRunMethodName)
		string runName;
		if (routePrefix.IsDefaultOrEmpty)
			runName = "Run_" + Naming.SanitizeIdentifier(commandName);
		else
		{
			var rnSb = new StringBuilder();
			rnSb.Append("Run");
			foreach (string seg in routePrefix) { rnSb.Append('_'); rnSb.Append(Naming.SanitizeIdentifier(seg)); }
			rnSb.Append('_'); rnSb.Append(Naming.SanitizeIdentifier(commandName));
			runName = rnSb.ToString();
		}
		INamedTypeSymbol? returnType = invokeMethod.ReturnType as INamedTypeSymbol;

		var cmd = new CommandModel(
			routePrefix,
			commandName,
			runName,
			"object",
			"__lambda",
			false,
			false,
			returnType,
			parameters,
			null,
			"",
			"",
			"",
			usage,
			ImmutableArray<INamedTypeSymbol>.Empty,
			IsLambda: true,
			LambdaStorageKey: storageKey,
			LambdaDelegateFq: delegateFq);

		targetNode.Commands.Add(cmd);
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
			foreach (INamedTypeSymbol nested in GetPublicNestedClasses(type))
			{
				string seg = Naming.ToTypeSegmentName(nested.Name);
				var childNode = new RegistryNode();
				ImmutableArray<string> nestedPrefix = AppendSegment(routePrefix, seg);
				ExpandTypeRegistration(context, invocation, nested, nestedPrefix, mergeOuterTypeSegment: true, childNode, parseOpts);
				attachTo.Children.Add(new RegistryNode.NamedCommandNamespaceChild { Segment = seg, Node = childNode });
			}
		}
		else
		{
			string seg = Naming.ToTypeSegmentName(type.Name);
			var wrapper = new RegistryNode();
			ImmutableArray<string> outerPrefix = AppendSegment(routePrefix, seg);
			ExpandTypeRegistration(context, invocation, type, outerPrefix, mergeOuterTypeSegment: true, wrapper, parseOpts);
			attachTo.Children.Add(new RegistryNode.NamedCommandNamespaceChild { Segment = seg, Node = wrapper });
		}
	}

	private static ImmutableArray<string> AppendSegment(ImmutableArray<string> prefix, string segment)
	{
		ImmutableArray<string>.Builder b = ImmutableArray.CreateBuilder<string>(prefix.Length + 1);
		foreach (string s in prefix)
			b.Add(s);
		b.Add(segment);
		return b.MoveToImmutable();
	}

	private static IEnumerable<INamedTypeSymbol> GetPublicNestedClasses(INamedTypeSymbol type)
	{
		foreach (ISymbol member in type.GetMembers())
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
		foreach (ISymbol member in type.GetMembers())
		{
			if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
				continue;

			if (method.AssociatedSymbol is not null)
				continue;

			if (method.DeclaredAccessibility != Accessibility.Public)
				continue;

			string cmdName = Naming.ToCommandName(method.Name);
			targetNode.Commands.Add(CommandModel.FromMethod(cmdName, method, parseOpts, routePrefix, context, invocation.GetLocation()));
		}
	}

	private static OptionsTypeModel? BuildOptionsTypeModel(SourceProductionContext context, INamedTypeSymbol type)
	{
		var members = ImmutableArray.CreateBuilder<ParameterModel>();
		foreach (ISymbol member in type.GetMembers())
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

		if (members.Count == 0)
			return new OptionsTypeModel(type, ImmutableArray<ParameterModel>.Empty);

		return new OptionsTypeModel(type, members.ToImmutable());
	}

	private static IMethodSymbol? ResolveHandlerMethod(
		SemanticModel model,
		ExpressionSyntax handlerExpr,
		SourceProductionContext context,
		InvocationExpressionSyntax invocation)
	{
		ISymbol? symbol = model.GetSymbolInfo(handlerExpr).Symbol;
		switch (symbol)
		{
			case IMethodSymbol m:
				return m;
			case IFieldSymbol { IsStatic: true, ConstantValue: { } }:
				context.ReportDiagnostic(Diagnostic.Create(HandlerMustBeMethod, handlerExpr.GetLocation()));
				return null;
		}

		IOperation? op = model.GetOperation(handlerExpr);
		while (op is IConversionOperation conv)
			op = conv.Operand;

		if (op is IMethodReferenceOperation directRef)
			return directRef.Method;

		if (op is IDelegateCreationOperation del && del.Target is IMethodReferenceOperation reference)
			return reference.Method;

		context.ReportDiagnostic(Diagnostic.Create(HandlerMustBeMethod, handlerExpr.GetLocation()));
		return null;
	}

	private static void CollectGlobalFilters(
		SourceProductionContext context,
		Compilation compilation,
		List<InvocationExpressionSyntax> rootInvocations,
		out ImmutableArray<GlobalFilterRegistration> globalFilters)
	{
		var b = ImmutableArray.CreateBuilder<GlobalFilterRegistration>();
		foreach (InvocationExpressionSyntax inv in rootInvocations)
		{
			SemanticModel model = compilation.GetSemanticModel(inv.SyntaxTree);
			if (model.GetSymbolInfo(inv).Symbol is not IMethodSymbol method || method.Name != "UseFilter")
				continue;

			if (method.IsGenericMethod && method.TypeArguments.Length == 1 &&
			    method.TypeArguments[0] is INamedTypeSymbol gft && gft.TypeKind != TypeKind.Error)
			{
				b.Add(new GlobalFilterRegistration(gft));
				continue;
			}

			context.ReportDiagnostic(Diagnostic.Create(UseFilterDelegateNotSupported, inv.GetLocation()));
		}

		globalFilters = b.ToImmutable();
	}

	private static bool IsInjected(IParameterSymbol p)
	{
		ITypeSymbol t = p.Type;
		if (t is INamedTypeSymbol named && named.TypeKind == TypeKind.Struct)
		{
			string fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (fq == "global::System.Threading.CancellationToken")
				return true;
		}

		return false;
	}

	private static bool HasArgumentAttribute(IParameterSymbol p)
	{
		foreach (AttributeData attr in p.GetAttributes())
		{
			if (attr.AttributeClass?.Name == "ArgumentAttribute")
				return true;
		}

		return false;
	}

	private static bool HasArgumentAttribute(IPropertySymbol p)
	{
		foreach (AttributeData attr in p.GetAttributes())
		{
			if (attr.AttributeClass?.Name == "ArgumentAttribute")
				return true;
		}

		return false;
	}

	private static bool HasAsParametersAttribute(IParameterSymbol p)
	{
		foreach (AttributeData attr in p.GetAttributes())
		{
			if (attr.AttributeClass?.Name == "AsParametersAttribute")
				return true;
		}

		return false;
	}

	private static string? GetAsParametersPrefix(IParameterSymbol p)
	{
		foreach (AttributeData attr in p.GetAttributes())
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
				INamedTypeSymbol def = named.OriginalDefinition;
				string fq = def.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
		foreach (AttributeData attr in symbol.GetAttributes())
		{
			if (attr.AttributeClass?.Name != "CollectionSyntaxAttribute")
				continue;
			foreach (KeyValuePair<string, TypedConstant> na in attr.NamedArguments)
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
		foreach (ISymbol m in type.GetMembers())
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
		IMethodSymbol? set = prop.SetMethod;
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
		foreach (ParameterModel p in parameters)
		{
			if (p.Kind != ParameterKind.Flag)
				continue;
			void check(string name)
			{
				if (string.IsNullOrEmpty(name))
					return;
				if (seen.TryGetValue(name, out string? first))
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
			foreach (string al in p.Aliases)
				check(al);
			if (p.Special == BoolSpecialKind.NullableBool)
				check("no-" + p.CliLongName);
		}
	}

	private static void ValidateExpandedParameterLayout(SourceProductionContext context, Location location, ImmutableArray<ParameterModel> expanded)
	{
		bool seenFlag = false;
		foreach (ParameterModel p in expanded)
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

	private static ImmutableArray<ParameterModel> FlattenAsParametersType(
		SourceProductionContext context,
		Location location,
		IParameterSymbol methodParam,
		INamedTypeSymbol type,
		string? prefix,
		CSharpParseOptions parseOptions)
	{
		string pfx = string.IsNullOrWhiteSpace(prefix) ? "" : Naming.ToCliLongName(prefix!.Trim()) + "-";
		string owner = methodParam.Name;
		string typeFq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		IMethodSymbol? primary = TryGetPrimaryConstructor(type);
		var ctorNames = new HashSet<string>(StringComparer.Ordinal);
		var list = new List<ParameterModel>();
		int order = 0;

		if (primary is not null)
		{
			foreach (IParameterSymbol cp in primary.Parameters)
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
		for (INamedTypeSymbol? t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
			chain.Add(t);

		var seenPropNames = new HashSet<string>(StringComparer.Ordinal);
		for (int i = chain.Count - 1; i >= 0; i--)
		{
			INamedTypeSymbol tt = chain[i];
			foreach (ISymbol member in tt.GetMembers())
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

	private static void EmitEmpty(SourceProductionContext context, string assemblyName, string assemblyVersion)
	{
		const string source = """
			// <auto-generated/>
			#nullable enable
			using System;
			using System.Threading.Tasks;

			namespace Nullean.Argh
			{
				/// <summary>Source-generated CLI entry point from <c>ArghApp</c> registrations. At the root, <c>--completions bash|zsh|fish</c> prints a shell script from <see cref="CompletionScriptTemplates"/>.</summary>
				public static class ArghGenerated
				{
					public static Task<int> RunAsync(string[] args) =>
						Task.FromResult(Run(args));

					public static bool TryParseRoute(string[] args, out global::Nullean.Argh.RouteMatch match)
					{
						match = default;
						return false;
					}

					public static global::Nullean.Argh.RouteMatch? Route(string commandLine)
					{
						if (commandLine is null)
							throw new ArgumentNullException(nameof(commandLine));
						var args = global::Nullean.Argh.ArghCli.SplitCommandLine(commandLine);
						if (!TryParseRoute(args, out var m))
							return null;
						return m;
					}

					private static int Run(string[] args)
					{
						if (args.Length >= 2 && args[0] == "--completions")
						{
							var shell = args[1];
							var appName = "__ARGH_EMBED_ASM_NAME__";
							if (string.Equals(shell, "bash", System.StringComparison.OrdinalIgnoreCase))
							{
								System.Console.Out.Write(CompletionScriptTemplates.GetBash().Replace("{0}", appName));
								return 0;
							}
							if (string.Equals(shell, "zsh", System.StringComparison.OrdinalIgnoreCase))
							{
								System.Console.Out.Write(CompletionScriptTemplates.GetZsh().Replace("{0}", appName));
								return 0;
							}
							if (string.Equals(shell, "fish", System.StringComparison.OrdinalIgnoreCase))
							{
								System.Console.Out.Write(CompletionScriptTemplates.GetFish().Replace("{0}", appName));
								return 0;
							}
							System.Console.Error.WriteLine($"Error: unsupported shell '{shell}' for --completions (expected bash, zsh, or fish).");
							return 2;
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

					private static void PrintVersion()
					{
						System.Console.Out.WriteLine("__ARGH_EMBED_ASM_VER__");
					}
				}

				internal static class ArghGeneratedRuntimeRegistration
				{
					[System.Runtime.CompilerServices.ModuleInitializer]
					internal static void RegisterArghRuntime()
					{
						global::Nullean.Argh.ArghRuntime.RegisterRunner(ArghGenerated.RunAsync);
						global::Nullean.Argh.ArghRuntime.RegisterRoute(ArghGenerated.Route);
					}
				}
			}
			""";
		string resolved = source
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
		sb.AppendLine("\t\t\tglobal::Nullean.Argh.ArghRuntime.RegisterRunner(ArghGenerated.RunAsync);");
		sb.AppendLine("\t\t\tglobal::Nullean.Argh.ArghRuntime.RegisterRoute(ArghGenerated.Route);");
		sb.AppendLine("\t\t}");
		sb.AppendLine("\t}");
	}

	private const int FuzzyMaxDistance = 2;

	private static void EmitRootCompletionsBlock(StringBuilder sb, string indent, string entryAssemblyName)
	{
		sb.AppendLine(indent + "if (args.Length >= 2 && args[0] == \"--completions\")");
		sb.AppendLine(indent + "{");
		sb.AppendLine(indent + "\tvar __shell = args[1];");
		sb.AppendLine(indent + "\tvar __entry = \"" + Escape(entryAssemblyName) + "\";");
		sb.AppendLine(indent + "\tif (string.Equals(__shell, \"bash\", StringComparison.OrdinalIgnoreCase))");
		sb.AppendLine(indent + "\t{");
		sb.AppendLine(indent + "\t\tConsole.Out.Write(global::Nullean.Argh.CompletionScriptTemplates.GetBash().Replace(\"{0}\", __entry));");
		sb.AppendLine(indent + "\t\treturn 0;");
		sb.AppendLine(indent + "\t}");
		sb.AppendLine(indent + "\tif (string.Equals(__shell, \"zsh\", StringComparison.OrdinalIgnoreCase))");
		sb.AppendLine(indent + "\t{");
		sb.AppendLine(indent + "\t\tConsole.Out.Write(global::Nullean.Argh.CompletionScriptTemplates.GetZsh().Replace(\"{0}\", __entry));");
		sb.AppendLine(indent + "\t\treturn 0;");
		sb.AppendLine(indent + "\t}");
		sb.AppendLine(indent + "\tif (string.Equals(__shell, \"fish\", StringComparison.OrdinalIgnoreCase))");
		sb.AppendLine(indent + "\t{");
		sb.AppendLine(indent + "\t\tConsole.Out.Write(global::Nullean.Argh.CompletionScriptTemplates.GetFish().Replace(\"{0}\", __entry));");
		sb.AppendLine(indent + "\t\treturn 0;");
		sb.AppendLine(indent + "\t}");
		sb.AppendLine(indent + "\tConsole.Error.WriteLine($\"Error: unsupported shell '{__shell}' for --completions (expected bash, zsh, or fish).\");");
		sb.AppendLine(indent + "\treturn 2;");
		sb.AppendLine(indent + "}");
		sb.AppendLine();
	}

	private static void EmitFuzzyFlatSwitchDefault(StringBuilder sb, ImmutableArray<CommandModel> commands, string entryAssemblyName)
	{
		List<CommandModel> sorted = commands.OrderBy(c => c.CommandName, StringComparer.Ordinal).ToList();
		sb.AppendLine("\t\t\t\tvar __tok = args[0];");
		sb.AppendLine("\t\t\t\tvar __app = \"" + Escape(entryAssemblyName) + "\";");
		sb.Append("\t\t\t\tvar __cands = new string[] { ");
		for (int i = 0; i < sorted.Count; i++)
		{
			if (i > 0)
				sb.Append(", ");
			sb.Append('"').Append(Escape(sorted[i].CommandName)).Append('"');
		}

		sb.AppendLine(" };");
		sb.AppendLine($"\t\t\t\tvar __matches = global::Nullean.Argh.FuzzyMatch.FindClosest(__tok, __cands, {FuzzyMaxDistance});");
		sb.AppendLine("\t\t\t\tif (__matches.Count == 0)");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown command '{__tok}'.\");");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\telse if (__matches.Count == 1)");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\tvar __m = __matches[0];");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown command '{__tok}'. Did you mean '{__m}'?\");");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Run '{__app} {__m} --help' for usage.\");");
		sb.AppendLine("\t\t\t\t\tConsole.Out.WriteLine();");
		foreach (CommandModel c in sorted)
		{
			sb.AppendLine($"\t\t\t\t\tif (string.Equals(__m, \"{Escape(c.CommandName)}\", StringComparison.OrdinalIgnoreCase))");
			sb.AppendLine("\t\t\t\t\t{");
			sb.AppendLine($"\t\t\t\t\t\tPrintHelp_{c.RunMethodName}();");
			sb.AppendLine("\t\t\t\t\t\treturn 2;");
			sb.AppendLine("\t\t\t\t\t}");
		}

		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\telse");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown command '{__tok}'. Did you mean one of these?\");");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
		foreach (CommandModel c in sorted)
		{
			string sum = Escape(c.SummaryOneLiner);
			sb.AppendLine(
				$"\t\t\t\t\tif (__matches.Any(__x => string.Equals(__x, \"{Escape(c.CommandName)}\", StringComparison.OrdinalIgnoreCase)))");
			sb.AppendLine("\t\t\t\t\t{");
			sb.AppendLine(
				$"\t\t\t\t\t\tConsole.Error.WriteLine(\"  \" + CliHelpFormatting.Accent(\"{Escape(c.CommandName)}\") + \"    {sum}\");");
			sb.AppendLine("\t\t\t\t\t}");
		}

		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Run '{__app} <command> --help' for usage.\");");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\treturn 2;");
	}

	private static void EmitFuzzyDispatchDefault(
		StringBuilder sb,
		RegistryNode node,
		ImmutableArray<string> path,
		string entryAssemblyName)
	{
		var entries = new List<(string Name, string Summary, string HelpPrinter)>();
		foreach (CommandModel cmd in node.Commands)
			entries.Add((cmd.CommandName, cmd.SummaryOneLiner, $"PrintHelp_{cmd.RunMethodName}"));
		foreach (RegistryNode.NamedCommandNamespaceChild ch in node.Children)
		{
			ImmutableArray<string> childPath = AppendSegment(path, ch.Segment);
			string gk = CommandNamespacePathKey(childPath);
			entries.Add((ch.Segment, "", $"PrintHelp_CommandNamespace_{gk}"));
		}

		List<(string Name, string Summary, string HelpPrinter)> sorted =
			entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();

		sb.AppendLine("\t\t\t\tvar __tok = tok;");
		sb.AppendLine("\t\t\t\tvar __app = \"" + Escape(entryAssemblyName) + "\";");
		sb.Append("\t\t\t\tvar __cands = new string[] { ");
		for (int i = 0; i < sorted.Count; i++)
		{
			if (i > 0)
				sb.Append(", ");
			sb.Append('"').Append(Escape(sorted[i].Name)).Append('"');
		}

		sb.AppendLine(" };");
		sb.AppendLine($"\t\t\t\tvar __matches = global::Nullean.Argh.FuzzyMatch.FindClosest(__tok, __cands, {FuzzyMaxDistance});");
		const string kind = "command or namespace";
		sb.AppendLine("\t\t\t\tif (__matches.Count == 0)");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown {kind} '{{__tok}}'.\");");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\telse if (__matches.Count == 1)");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine("\t\t\t\t\tvar __m = __matches[0];");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown {kind} '{{__tok}}'. Did you mean '{{__m}}'?\");");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Run '{__app} <command> --help' for usage.\");");
		sb.AppendLine("\t\t\t\t\tConsole.Out.WriteLine();");
		foreach (var e in sorted)
		{
			sb.AppendLine($"\t\t\t\t\tif (string.Equals(__m, \"{Escape(e.Name)}\", StringComparison.OrdinalIgnoreCase))");
			sb.AppendLine("\t\t\t\t\t{");
			sb.AppendLine($"\t\t\t\t\t\t{e.HelpPrinter}();");
			sb.AppendLine("\t\t\t\t\t\treturn 2;");
			sb.AppendLine("\t\t\t\t\t}");
		}

		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\telse");
		sb.AppendLine("\t\t\t\t{");
		sb.AppendLine($"\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown {kind} '{{__tok}}'. Did you mean one of these?\");");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
		foreach (var e in sorted)
		{
			string sum = Escape(e.Summary);
			sb.AppendLine(
				$"\t\t\t\t\tif (__matches.Any(__x => string.Equals(__x, \"{Escape(e.Name)}\", StringComparison.OrdinalIgnoreCase)))");
			sb.AppendLine("\t\t\t\t\t{");
			sb.AppendLine(
				$"\t\t\t\t\t\tConsole.Error.WriteLine(\"  \" + CliHelpFormatting.Accent(\"{Escape(e.Name)}\") + \"    {sum}\");");
			sb.AppendLine("\t\t\t\t\t}");
		}

		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine();");
		sb.AppendLine("\t\t\t\t\tConsole.Error.WriteLine($\"Run '{__app} <command> --help' for usage.\");");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\treturn 2;");
	}

	private static bool IsFlatCli(AppEmitModel app)
	{
		if (app.GlobalOptionsType is not null)
			return false;
		if (app.Root.Children.Count > 0)
			return false;
		foreach (CommandModel c in app.AllCommands)
		{
			if (!c.RoutePrefix.IsDefaultOrEmpty)
				return false;
		}

		return true;
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

	private static bool HasPublicParameterlessCtor(INamedTypeSymbol type)
	{
		foreach (IMethodSymbol ctor in type.InstanceConstructors)
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
		string entryAssemblyVersion)
	{
		ImmutableArray<DtoBindingTarget> dtoTargets = CollectDtoBindingTargets(context, app, parseOptions);
		if (IsFlatCli(app))
		{
			EmitFlat(context, app, dtoTargets, entryAssemblyName, entryAssemblyVersion);
			EmitDtoTypeExtensions(context, dtoTargets);
			return;
		}

		EmitHierarchical(context, app, dtoTargets, entryAssemblyName, entryAssemblyVersion);
		EmitDtoTypeExtensions(context, dtoTargets);
	}

	private sealed record DtoBindingTarget(INamedTypeSymbol TypeSymbol, ImmutableArray<ParameterModel> Members, bool IsOptionsDto);

	private static ImmutableArray<DtoBindingTarget> CollectDtoBindingTargets(
		SourceProductionContext context,
		AppEmitModel app,
		CSharpParseOptions parseOptions)
	{
		var map = new Dictionary<INamedTypeSymbol, DtoBindingTarget>(SymbolEqualityComparer.Default);

		if (app.GlobalOptionsType is not null)
		{
			ImmutableArray<ParameterModel> m = BuildFlattenedOptionsMembers(app.GlobalOptionsType);
			if (m.Length > 0)
				map[app.GlobalOptionsType] = new DtoBindingTarget(app.GlobalOptionsType, m, IsOptionsDto: true);
		}

		foreach ((RegistryNode node, _) in EnumerateCommandNamespaceNodesWithPath(app.Root, ImmutableArray<string>.Empty))
		{
			if (node.CommandNamespaceOptionsType is null)
				continue;

			ImmutableArray<ParameterModel> gm = BuildFlattenedOptionsMembers(node.CommandNamespaceOptionsType);
			if (gm.Length > 0)
				map[node.CommandNamespaceOptionsType] = new DtoBindingTarget(node.CommandNamespaceOptionsType, gm, IsOptionsDto: true);
		}

		foreach (CommandModel cmd in app.AllCommands)
		{
			if (cmd.HandlerMethod is null)
				continue;

			foreach (IParameterSymbol p in cmd.HandlerMethod.Parameters)
			{
				if (!HasAsParametersAttribute(p))
					continue;

				if (p.Type is not INamedTypeSymbol nt || nt.TypeKind == TypeKind.Error)
					continue;

				if (map.ContainsKey(nt))
					continue;

				ImmutableArray<ParameterModel> flat = FlattenAsParametersType(
					context,
					Location.None,
					p,
					nt,
					GetAsParametersPrefix(p),
					parseOptions);
				if (flat.Length > 0)
					map[nt] = new DtoBindingTarget(nt, flat, IsOptionsDto: false);
			}
		}

		return map.Values
			.OrderBy(t => t.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
			.ToImmutableArray();
	}

	private static ImmutableArray<ParameterModel> BuildFlattenedOptionsMembers(INamedTypeSymbol type)
	{
		var chain = new List<INamedTypeSymbol>();
		for (INamedTypeSymbol? t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
			chain.Add(t);

		var members = ImmutableArray.CreateBuilder<ParameterModel>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = chain.Count - 1; i >= 0; i--)
		{
			INamedTypeSymbol tt = chain[i];
			foreach (ISymbol member in tt.GetMembers())
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

	private static string DtoMethodSuffix(INamedTypeSymbol type)
	{
		string fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		if (fq.StartsWith("global::", StringComparison.Ordinal))
			fq = fq.Substring(8);

		var sb = new StringBuilder();
		foreach (char c in fq)
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
		foreach (DtoBindingTarget t in targets)
		{
			string methodName = "TryParseDto_" + DtoMethodSuffix(t.TypeSymbol);
			string resultFq = t.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			CommandModel syn = SyntheticOptionsCommand(t.Members, methodName);
			EmitCommandRunner(
				sb,
				syn,
				ImmutableArray<GlobalFilterRegistration>.Empty,
				emitDtoTryParse: true,
				dtoMethodName: methodName,
				dtoResultTypeFq: resultFq,
				dtoOptionsType: t.IsOptionsDto ? t.TypeSymbol : null);
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
		foreach (DtoBindingTarget t in targets)
		{
			string fq = t.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			string method = "TryParseDto_" + DtoMethodSuffix(t.TypeSymbol);
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

		foreach (DtoBindingTarget t in targets)
		{
			if (t.TypeSymbol.TypeParameters.Length > 0)
				continue;

			string fq = t.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			string method = "TryParseDto_" + DtoMethodSuffix(t.TypeSymbol);
			string vis = t.TypeSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
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

	private static void EmitFlat(SourceProductionContext context, AppEmitModel app, ImmutableArray<DtoBindingTarget> dtoTargets, string entryAssemblyName, string entryAssemblyVersion)
	{
		ImmutableArray<CommandModel> commands = app.AllCommands;
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
		sb.AppendLine();
		sb.AppendLine("namespace Nullean.Argh");
		sb.AppendLine("{");
		sb.AppendLine("\t/// <summary>Source-generated CLI entry point from <c>ArghApp</c> registrations. At the root, <c>--completions bash|zsh|fish</c> prints a shell script from <see cref=\"CompletionScriptTemplates\"/>; each <c>{0}</c> in the template is replaced with the entry assembly name (same effect as <c>string.Format</c>, but substitution uses <c>Replace</c> so shell scripts can contain literal braces).</summary>");
		sb.AppendLine("\tpublic static class ArghGenerated");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tpublic static Task<int> RunAsync(string[] args) =>");
		sb.AppendLine("\t\t\tRunWithCancellationAsync(args);");
		sb.AppendLine();
		AppendRunWithCancellationAsyncMethod(sb);
		sb.AppendLine("\t\tprivate static async Task<int> RunCoreAsync(string[] args, CancellationToken ct)");
		sb.AppendLine("\t\t{");
		EmitRootCompletionsBlock(sb, "\t\t\t", entryAssemblyName);
		sb.AppendLine("\t\t\tif (args.Length == 0)");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tPrintRootHelp();");
		sb.AppendLine("\t\t\t\treturn 0;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine();
		sb.AppendLine("\t\t\tif (args[0] == \"--help\" || args[0] == \"-h\")");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tPrintRootHelp();");
		sb.AppendLine("\t\t\t\treturn 0;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine();
		sb.AppendLine("\t\t\tif (args[0] == \"--version\")");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tPrintVersion();");
		sb.AppendLine("\t\t\t\treturn 0;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine();
		sb.AppendLine("\t\t\tswitch (args[0])");
		sb.AppendLine("\t\t\t{");

		foreach (CommandModel cmd in commands)
		{
			sb.AppendLine($"\t\t\t\tcase \"{Escape(cmd.CommandName)}\":");
			sb.AppendLine($"\t\t\t\t\treturn await {cmd.RunMethodName}(Tail(args), ct).ConfigureAwait(false);");
		}

		sb.AppendLine("\t\t\t\tdefault:");
		EmitFuzzyFlatSwitchDefault(sb, commands, entryAssemblyName);
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		EmitPrintRootHelpFlat(sb, commands, entryAssemblyName);
		sb.AppendLine();
		foreach (CommandModel cmd in commands)
			EmitCommandHelpPrinter(sb, cmd, app, entryAssemblyName);

		sb.AppendLine();
		sb.AppendLine("\t\tprivate static void PrintVersion()");
		sb.AppendLine("\t\t{");
		sb.AppendLine($"			Console.Out.WriteLine(\"{Escape(entryAssemblyVersion)}\");");
		sb.AppendLine("\t\t}");
		sb.AppendLine();

		foreach (CommandModel cmd in commands)
			EmitCommandRunner(sb, cmd, app.GlobalFilters);

		sb.AppendLine("\t\tprivate static bool? ParseNullableBool(string? raw, bool fromYesSwitch)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tif (string.IsNullOrEmpty(raw)) return fromYesSwitch;");
		sb.AppendLine("\t\t\tif (bool.TryParse(raw, out var b)) return b;");
		sb.AppendLine("\t\t\treturn fromYesSwitch;");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		sb.AppendLine("\t\tprivate static string[] Tail(string[] args)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tif (args.Length <= 1) return Array.Empty<string>();");
		sb.AppendLine("\t\t\tvar tail = new string[args.Length - 1];");
		sb.AppendLine("\t\t\tArray.Copy(args, 1, tail, 0, tail.Length);");
		sb.AppendLine("\t\t\treturn tail;");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		EmitFlatTryParseRoute(sb, commands);
		EmitArghGeneratedRouteStringMethod(sb);
		EmitDtoBindingMethods(sb, dtoTargets);
		sb.AppendLine("\t}");
		AppendArghRuntimeModuleInitializer(sb);
		sb.AppendLine("}");
		context.AddSource("ArghGenerated.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}

	private static void EmitPrintRootHelpFlat(StringBuilder sb, ImmutableArray<CommandModel> commands, string entryAssemblyName)
	{
		sb.AppendLine("\t\tprivate static void PrintRootHelp()");
		sb.AppendLine("\t\t{");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Usage: \") + CliHelpFormatting.Accent(\"{Escape(entryAssemblyName)}\") + \" <command> [options]\");");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Commands:\"));");
		foreach (CommandModel c in commands)
		{
			string summary = Escape(c.SummaryOneLiner);
			sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(c.CommandName)}\")}}    {summary}\");");
		}

		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Global options:\"));");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine(\"  --help, -h    Show help.\");");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine(\"  --version     Show version.\");");
		sb.AppendLine("\t\t}");
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
		sb.AppendLine();
		sb.AppendLine("namespace Nullean.Argh");
		sb.AppendLine("{");
		sb.AppendLine("\t/// <summary>Source-generated CLI entry point from <c>ArghApp</c> registrations. At the root, <c>--completions bash|zsh|fish</c> prints a shell script from <see cref=\"CompletionScriptTemplates\"/>; each <c>{0}</c> in the template is replaced with the entry assembly name (same effect as <c>string.Format</c>, but substitution uses <c>Replace</c> so shell scripts can contain literal braces).</summary>");
		sb.AppendLine("\tpublic static class ArghGenerated");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tpublic static Task<int> RunAsync(string[] args) =>");
		sb.AppendLine("\t\t\tRunWithCancellationAsync(args);");
		sb.AppendLine();
		AppendRunWithCancellationAsyncMethod(sb);
		EmitRunCoreHierarchical(sb, app, entryAssemblyName);
		sb.AppendLine();
		EmitPrintRootHelpHierarchical(sb, app, entryAssemblyName);
		sb.AppendLine();
		foreach ((RegistryNode node, ImmutableArray<string> path) in EnumerateCommandNamespaceNodesWithPath(app.Root, ImmutableArray<string>.Empty))
			EmitCommandNamespaceHelpPrinter(sb, path, node, app, entryAssemblyName);

		foreach (CommandModel cmd in app.AllCommands)
			EmitCommandHelpPrinter(sb, cmd, app, entryAssemblyName);

		sb.AppendLine("\t\tprivate static void PrintVersion()");
		sb.AppendLine("\t\t{");
		sb.AppendLine($"			Console.Out.WriteLine(\"{Escape(entryAssemblyVersion)}\");");
		sb.AppendLine("\t\t}");
		sb.AppendLine();

		foreach (CommandModel cmd in app.AllCommands)
			EmitCommandRunner(sb, cmd, app.GlobalFilters);

		if (app.GlobalOptionsModel is { Members: { Length: > 0 } })
			EmitOptionsTryParse(sb, "TryParseGlobalOptions", app.GlobalOptionsModel.Members);

		foreach ((RegistryNode node, ImmutableArray<string> path) in EnumerateCommandNamespaceNodesWithPath(app.Root, ImmutableArray<string>.Empty))
		{
			if (node.CommandNamespaceOptionsModel is OptionsTypeModel gopt && gopt.Members.Length > 0)
				EmitOptionsTryParse(sb, CommandNamespaceOptionsParseMethodName(path), gopt.Members);
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
		sb.AppendLine("\t}");
		AppendArghRuntimeModuleInitializer(sb);
		sb.AppendLine("}");
		context.AddSource("ArghGenerated.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}

	private static IEnumerable<(RegistryNode node, ImmutableArray<string> path)> EnumerateCommandNamespaceNodesWithPath(
		RegistryNode root,
		ImmutableArray<string> prefix)
	{
		foreach (RegistryNode.NamedCommandNamespaceChild ch in root.Children)
		{
			ImmutableArray<string> p = AppendSegment(prefix, ch.Segment);
			yield return (ch.Node, p);
			foreach ((RegistryNode node, ImmutableArray<string> sub) in EnumerateCommandNamespaceNodesWithPath(ch.Node, p))
				yield return (node, sub);
		}
	}

	private static string CommandNamespacePathKey(ImmutableArray<string> path)
	{
		if (path.IsDefaultOrEmpty)
			return "Root";

		var sb = new StringBuilder();
		for (int i = 0; i < path.Length; i++)
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
		bool hasGlobal = app.GlobalOptionsModel is { Members: { Length: > 0 } };
		sb.AppendLine("\t\tprivate static async Task<int> RunCoreAsync(string[] args, CancellationToken ct)");
		sb.AppendLine("\t\t{");
		EmitRootCompletionsBlock(sb, "\t\t\t", entryAssemblyName);
		sb.AppendLine("\t\t\tvar idx = new int[1];");
		if (hasGlobal)
			sb.AppendLine("\t\t\tif (!TryParseGlobalOptions(args, idx)) return 2;");

		sb.AppendLine("\t\t\tif (args.Length == 0)");
		sb.AppendLine("\t\t\t{");
		sb.AppendLine("\t\t\t\tPrintRootHelp();");
		sb.AppendLine("\t\t\t\treturn 0;");
		sb.AppendLine("\t\t\t}");
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
		sb.AppendLine("\t\t\t\tPrintRootHelp();");
		sb.AppendLine("\t\t\t\treturn 0;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t\treturn await DispatchRoot(args, idx, ct).ConfigureAwait(false);");
		sb.AppendLine("\t\t}");
	}

	private static void EmitPrintRootHelpHierarchical(StringBuilder sb, AppEmitModel app, string entryAssemblyName)
	{
		sb.AppendLine("\t\tprivate static void PrintRootHelp()");
		sb.AppendLine("\t\t{");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Usage: \") + CliHelpFormatting.Accent(\"{Escape(entryAssemblyName)}\") + \" <namespace|command> [options]\");");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		if (app.Root.Children.Count > 0)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Namespaces:\"));");
			foreach (RegistryNode.NamedCommandNamespaceChild ch in app.Root.Children)
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(ch.Segment)}\")}}\");");

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		if (app.Root.Commands.Count > 0)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Commands:\"));");
			foreach (CommandModel c in app.Root.Commands)
			{
				string summary = Escape(c.SummaryOneLiner);
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(c.CommandName)}\")}}    {summary}\");");
			}

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Global options:\"));");
		if (app.GlobalOptionsModel is OptionsTypeModel gom && gom.Members.Length > 0)
		{
			foreach (ParameterModel p in gom.Members)
			{
				if (p.Kind != ParameterKind.Flag)
					continue;
				string left = HelpLayout.FormatOptionLeftCell(p);
				string desc = BuildDescriptionSuffix(p, forPositional: false);
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {Escape(desc)}\");");
			}
		}

		sb.AppendLine("\t\t\tConsole.Out.WriteLine(\"  \" + CliHelpFormatting.Accent(\"--help, -h\") + \"  Show help.\");");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine(\"  \" + CliHelpFormatting.Accent(\"--version\") + \"  Show version.\");");
		sb.AppendLine("\t\t}");
	}

	private static void EmitCommandNamespaceHelpPrinter(StringBuilder sb, ImmutableArray<string> path, RegistryNode node, AppEmitModel app, string entryAssemblyName)
	{
		string key = CommandNamespacePathKey(path);
		string usagePrefix = string.Join(" ", path);

		List<ParameterModel> globalFlagMembers = EnumerateFlagMembers(app.GlobalOptionsModel).ToList();
		List<(string Segment, List<ParameterModel> Rows)> namespaceOptionSections = new();
		List<(string Segment, OptionsTypeModel Model)> namespaceOptionChain = GetCommandNamespaceOptionChain(app, path);
		var suppressedForNamespaceDisplay = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddCliKeys(globalFlagMembers, suppressedForNamespaceDisplay);
		foreach ((string seg, OptionsTypeModel gom) in namespaceOptionChain)
		{
			List<ParameterModel> allInNamespace = EnumerateFlagMembers(gom).ToList();
			List<ParameterModel> rows = allInNamespace.Where(p => !suppressedForNamespaceDisplay.Contains(p.CliLongName)).ToList();
			AddCliKeys(allInNamespace, suppressedForNamespaceDisplay);
			if (rows.Count > 0)
				namespaceOptionSections.Add((seg, rows));
		}

		var widthCandidates = new List<int> { "--help, -h".Length };
		widthCandidates.AddRange(globalFlagMembers.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		foreach ((_, List<ParameterModel> rows) in namespaceOptionSections)
			widthCandidates.AddRange(rows.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		int maxOptWidth = Math.Min(widthCandidates.Max(), 40);
		maxOptWidth = Math.Max(maxOptWidth, "--help, -h".Length);

		sb.AppendLine($"\t\tprivate static void PrintHelp_CommandNamespace_{key}()");
		sb.AppendLine("\t\t{");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Usage: \") + CliHelpFormatting.Accent(\"{Escape(entryAssemblyName)}\") + \" {Escape(usagePrefix)} <command> [options]\");");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");

		if (node.Children.Count > 0)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Namespaces:\"));");
			foreach (RegistryNode.NamedCommandNamespaceChild ch in node.Children)
			{
				string fullNs = FormatQualifiedCliPath(path, ch.Segment);
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(fullNs)}\")}}\");");
			}

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		if (node.Commands.Count > 0)
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Commands:\"));");
			foreach (CommandModel c in node.Commands)
			{
				string fullCmd = FormatQualifiedCliPath(path, c.CommandName);
				string summary = Escape(c.SummaryOneLiner);
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(fullCmd)}\")}}    {summary}\");");
			}

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Global options:\"));");
		if (globalFlagMembers.Count > 0)
			EmitHelpOptionRows(sb, globalFlagMembers, maxOptWidth);
		string globalHelpLeft = "--help, -h".PadRight(maxOptWidth);
		sb.AppendLine($"\t\t\tConsole.Out.WriteLine(\"  \" + CliHelpFormatting.Accent(\"{Escape(globalHelpLeft)}\") + \"  Show help.\");");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");

		foreach ((string segment, List<ParameterModel> gRows) in namespaceOptionSections)
		{
			sb.AppendLine($"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"'{Escape(segment)}' options:\"));");
			EmitHelpOptionRows(sb, gRows, maxOptWidth);
			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
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
		if (!isRoot && node.CommandNamespaceOptionsModel is { Members: { Length: > 0 } })
			sb.AppendLine($"\t\t\tif (!{CommandNamespaceOptionsParseMethodName(path)}(args, idx)) return 2;");

		sb.AppendLine("\t\t\tif (idx[0] >= args.Length)");
		sb.AppendLine("\t\t\t{");
		if (isRoot)
			sb.AppendLine("\t\t\t\tPrintRootHelp();");
		else
			sb.AppendLine($"\t\t\t\tPrintHelp_CommandNamespace_{CommandNamespacePathKey(path)}();");

		sb.AppendLine("\t\t\t\treturn 0;");
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
		foreach (CommandModel cmd in node.Commands)
		{
			sb.AppendLine($"\t\t\tif (string.Equals(tok, \"{Escape(cmd.CommandName)}\", StringComparison.OrdinalIgnoreCase))");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tidx[0]++;");
			sb.AppendLine($"\t\t\t\treturn await {cmd.RunMethodName}(TailFrom(args, idx[0]), ct).ConfigureAwait(false);");
			sb.AppendLine("\t\t\t}");
		}

		foreach (RegistryNode.NamedCommandNamespaceChild ch in node.Children)
		{
			ImmutableArray<string> childPath = AppendSegment(path, ch.Segment);
			string childMethod = "DispatchCommandNamespace_" + CommandNamespacePathKey(childPath);
			sb.AppendLine($"\t\t\tif (string.Equals(tok, \"{Escape(ch.Segment)}\", StringComparison.OrdinalIgnoreCase))");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tidx[0]++;");
			sb.AppendLine($"\t\t\t\treturn await {childMethod}(args, idx, ct).ConfigureAwait(false);");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine("\t\t\t{");
		EmitFuzzyDispatchDefault(sb, node, path, entryAssemblyName);
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		foreach (RegistryNode.NamedCommandNamespaceChild ch in node.Children)
		{
			ImmutableArray<string> childPath = AppendSegment(path, ch.Segment);
			EmitDispatchForNode(sb, app, ch.Node, childPath, "DispatchCommandNamespace_" + CommandNamespacePathKey(childPath), isRoot: false, entryAssemblyName);
		}
	}

	private static string GetCommandRoutePath(CommandModel cmd)
	{
		if (cmd.RoutePrefix.IsDefaultOrEmpty)
			return cmd.CommandName;
		return string.Join("/", cmd.RoutePrefix) + "/" + cmd.CommandName;
	}

	/// <summary>Space-separated CLI path for help listings (e.g. <c>storage blob upload</c>).</summary>
	private static string FormatQualifiedCliPath(ImmutableArray<string> prefix, string segment)
	{
		if (prefix.IsDefaultOrEmpty)
			return segment;
		return string.Join(" ", prefix) + " " + segment;
	}

	private static void EmitArghGeneratedRouteStringMethod(StringBuilder sb)
	{
		sb.AppendLine("\t\tpublic static global::Nullean.Argh.RouteMatch? Route(string commandLine)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tif (commandLine is null)");
		sb.AppendLine("\t\t\t\tthrow new ArgumentNullException(nameof(commandLine));");
		sb.AppendLine("\t\t\tvar args = global::Nullean.Argh.ArghCli.SplitCommandLine(commandLine);");
		sb.AppendLine("\t\t\tif (!TryParseRoute(args, out var m))");
		sb.AppendLine("\t\t\t\treturn null;");
		sb.AppendLine("\t\t\treturn m;");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	private static void EmitFlatTryParseRoute(StringBuilder sb, ImmutableArray<CommandModel> commands)
	{
		sb.AppendLine("\t\tpublic static bool TryParseRoute(string[] args, out global::Nullean.Argh.RouteMatch match)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tmatch = default;");
		sb.AppendLine("\t\t\tif (args.Length >= 2 && args[0] == \"--completions\") return false;");
		sb.AppendLine("\t\t\tif (args.Length == 0) return false;");
		sb.AppendLine("\t\t\tif (args[0] == \"--help\" || args[0] == \"-h\") return false;");
		sb.AppendLine("\t\t\tif (args[0] == \"--version\") return false;");
		sb.AppendLine("\t\t\tswitch (args[0])");
		sb.AppendLine("\t\t\t{");
		foreach (CommandModel cmd in commands)
		{
			string path = Escape(GetCommandRoutePath(cmd));
			sb.AppendLine($"\t\t\t\tcase \"{Escape(cmd.CommandName)}\":");
			sb.AppendLine($"\t\t\t\t\tmatch = new global::Nullean.Argh.RouteMatch(\"{path}\", Tail(args));");
			sb.AppendLine("\t\t\t\t\treturn true;");
		}

		sb.AppendLine("\t\t\t\tdefault:");
		sb.AppendLine("\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	private static void EmitTryParseRouteHierarchical(StringBuilder sb, AppEmitModel app)
	{
		bool hasGlobal = app.GlobalOptionsModel is { Members: { Length: > 0 } };
		sb.AppendLine("\t\tpublic static bool TryParseRoute(string[] args, out global::Nullean.Argh.RouteMatch match)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tmatch = default;");
		sb.AppendLine("\t\t\tif (args.Length >= 2 && args[0] == \"--completions\") return false;");
		sb.AppendLine("\t\t\tvar idx = new int[1];");
		if (hasGlobal)
			sb.AppendLine("\t\t\tif (!TryParseGlobalOptions(args, idx)) return false;");
		sb.AppendLine("\t\t\tif (args.Length == 0) return false;");
		sb.AppendLine("\t\t\tif (idx[0] < args.Length && (args[idx[0]] == \"--help\" || args[idx[0]] == \"-h\")) return false;");
		sb.AppendLine("\t\t\tif (idx[0] < args.Length && args[idx[0]] == \"--version\") return false;");
		sb.AppendLine("\t\t\tif (idx[0] >= args.Length) return false;");
		sb.AppendLine("\t\t\treturn TryParseRouteRoot(args, idx, out match);");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		EmitTryParseRouteForNode(sb, app, app.Root, ImmutableArray<string>.Empty, "TryParseRouteRoot", isRoot: true);
		EmitArghGeneratedRouteStringMethod(sb);
	}

	private static void EmitTryParseRouteForNode(
		StringBuilder sb,
		AppEmitModel app,
		RegistryNode node,
		ImmutableArray<string> path,
		string methodName,
		bool isRoot)
	{
		sb.AppendLine($"\t\tprivate static bool {methodName}(string[] args, int[] idx, out global::Nullean.Argh.RouteMatch match)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tmatch = default;");
		if (!isRoot && node.CommandNamespaceOptionsModel is { Members: { Length: > 0 } })
			sb.AppendLine($"\t\t\tif (!{CommandNamespaceOptionsParseMethodName(path)}(args, idx)) return false;");
		sb.AppendLine("\t\t\tif (idx[0] >= args.Length) return false;");
		sb.AppendLine("\t\t\tif (args[idx[0]] == \"--help\" || args[idx[0]] == \"-h\") return false;");
		sb.AppendLine("\t\t\tvar tok = args[idx[0]];");
		foreach (CommandModel cmd in node.Commands)
		{
			string routePath = Escape(GetCommandRoutePath(cmd));
			sb.AppendLine($"\t\t\tif (string.Equals(tok, \"{Escape(cmd.CommandName)}\", StringComparison.OrdinalIgnoreCase))");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tidx[0]++;");
			sb.AppendLine($"\t\t\t\tmatch = new global::Nullean.Argh.RouteMatch(\"{routePath}\", TailFrom(args, idx[0]));");
			sb.AppendLine("\t\t\t\treturn true;");
			sb.AppendLine("\t\t\t}");
		}

		foreach (RegistryNode.NamedCommandNamespaceChild ch in node.Children)
		{
			ImmutableArray<string> childPath = AppendSegment(path, ch.Segment);
			string childMethod = "TryParseRouteCommandNamespace_" + CommandNamespacePathKey(childPath);
			sb.AppendLine($"\t\t\tif (string.Equals(tok, \"{Escape(ch.Segment)}\", StringComparison.OrdinalIgnoreCase))");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tidx[0]++;");
			sb.AppendLine($"\t\t\t\treturn {childMethod}(args, idx, out match);");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine("\t\t\treturn false;");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		foreach (RegistryNode.NamedCommandNamespaceChild ch in node.Children)
		{
			ImmutableArray<string> childPath = AppendSegment(path, ch.Segment);
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
			null,
			members,
			null,
			"",
			"",
			"",
			"",
			ImmutableArray<INamedTypeSymbol>.Empty);

	private static void EmitAllowedFlagPredicate(StringBuilder sb, ImmutableArray<ParameterModel> members)
	{
		sb.AppendLine("\t\t\tbool IsAllowedFlag(string name) => name switch");
		sb.AppendLine("\t\t\t{");
		foreach (ParameterModel p in members)
		{
			if (p.Kind != ParameterKind.Flag)
				continue;
			sb.AppendLine($"\t\t\t\t\"{Escape(p.CliLongName)}\" => true,");
			foreach (string al in p.Aliases)
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

	private static void EmitOptionsTryParse(StringBuilder sb, string methodName, ImmutableArray<ParameterModel> members)
	{
		CommandModel syn = SyntheticOptionsCommand(members, methodName);
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
		sb.AppendLine("\t\t\treturn true;");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	private static void EmitIsMultiFlagPredicate(StringBuilder sb, CommandModel cmd)
	{
		var names = new List<string>();
		foreach (ParameterModel p in cmd.Parameters)
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
		foreach (string n in names)
			sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");
	}

	private static void EmitBindCollectionParameter(StringBuilder sb, ParameterModel p, bool multiFlagsAvailable, string failureExit = "return 2", string? helpMethodName = null)
	{
		string flagKey = Escape(p.CliLongName);
		string acc = p.LocalVarName + "_acc";
		ParameterModel elemModel = ForElementParsing(p);
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

		string declType = p.FullDeclaredTypeFq ?? "object";
		if (p.CollectionTargetIsArray)
			sb.AppendLine($"\t\t\t{declType} {p.LocalVarName} = {acc}.ToArray();");
		else
			sb.AppendLine($"\t\t\t{declType} {p.LocalVarName} = {acc};");
	}

	private static void EmitAsParametersConstruction(StringBuilder sb, CommandModel cmd)
	{
		if (cmd.HandlerMethod is null)
			return;

		foreach (IParameterSymbol mp in cmd.HandlerMethod.Parameters)
		{
			if (!HasAsParametersAttribute(mp))
				continue;

			ParameterModel[] group = cmd.Parameters
				.Where(p => p.AsParametersOwnerParamName == mp.Name)
				.OrderBy(p => p.AsParametersMemberOrder)
				.ToArray();
			if (group.Length == 0)
				continue;

			string? typeFq = group[0].AsParametersTypeFq;
			if (typeFq is null)
				continue;

			string varName = AsParametersConstructedVarName(mp.Name);
			ParameterModel[] ctor = group.Where(p => !p.AsParametersUseInit).ToArray();
			ParameterModel[] init = group.Where(p => p.AsParametersUseInit).ToArray();
			sb.Append($"\t\t\tvar {varName} = new {typeFq}(");
			for (int i = 0; i < ctor.Length; i++)
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
				foreach (ParameterModel ip in init)
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
		ParameterModel[] group = cmd.Parameters
			.Where(static p => p.AsParametersOwnerParamName is not null)
			.OrderBy(static p => p.AsParametersMemberOrder)
			.ToArray();
		if (group.Length == 0)
		{
			sb.AppendLine("\t\t\treturn false;");
			return;
		}

		string? typeFq = group[0].AsParametersTypeFq;
		if (typeFq is null)
		{
			sb.AppendLine("\t\t\treturn false;");
			return;
		}

		ParameterModel[] ctor = group.Where(static p => !p.AsParametersUseInit).ToArray();
		ParameterModel[] init = group.Where(static p => p.AsParametersUseInit).ToArray();
		sb.Append("\t\t\tvar __dto = new ").Append(typeFq).Append("(");
		for (int i = 0; i < ctor.Length; i++)
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
			foreach (ParameterModel ip in init)
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

	private static void EmitOptionsDtoConstructionAndReturn(StringBuilder sb, INamedTypeSymbol type, ImmutableArray<ParameterModel> members)
	{
		string fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var byName = members.ToDictionary(static m => m.SymbolName, StringComparer.OrdinalIgnoreCase);

		IMethodSymbol? bestCtor = null;
		foreach (IMethodSymbol ctor in type.InstanceConstructors)
		{
			if (ctor.DeclaredAccessibility != Accessibility.Public)
				continue;
			if (ctor.Parameters.Length == 0)
				continue;
			if (!ctor.Parameters.All(p => byName.ContainsKey(p.Name)))
				continue;
			if (bestCtor is null || ctor.Parameters.Length > bestCtor.Parameters.Length)
				bestCtor = ctor;
		}

		if (bestCtor is not null && bestCtor.Parameters.Length > 0 && bestCtor.Parameters.Length == members.Length)
		{
			sb.Append("\t\t\tvalue = new ").Append(fq).Append("(");
			for (int i = 0; i < bestCtor.Parameters.Length; i++)
			{
				if (i > 0)
					sb.Append(", ");
				IParameterSymbol ps = bestCtor.Parameters[i];
				sb.Append(byName[ps.Name].LocalVarName);
			}

			sb.AppendLine(");");
			sb.AppendLine("\t\t\treturn true;");
			return;
		}

		sb.AppendLine($"\t\t\tvar __dto = new {fq}();");
		foreach (ParameterModel m in members)
			sb.AppendLine($"\t\t\t__dto.{m.SymbolName} = {m.LocalVarName};");

		sb.AppendLine("\t\t\tvalue = __dto;");
		sb.AppendLine("\t\t\treturn true;");
	}

	private static string AsParametersConstructedVarName(string methodParameterName) =>
		"__as_" + Naming.SanitizeIdentifier(methodParameterName);

	private static void EmitCommandRunner(
		StringBuilder sb,
		CommandModel cmd,
		ImmutableArray<GlobalFilterRegistration> globalFilters,
		bool emitDtoTryParse = false,
		string? dtoMethodName = null,
		string? dtoResultTypeFq = null,
		INamedTypeSymbol? dtoOptionsType = null)
	{
		bool anyRepeatedCollection = cmd.Parameters.Any(static p =>
			p is { IsCollection: true, Kind: ParameterKind.Flag } && p.CollectionSeparator is null);

		string failureExit = emitDtoTryParse ? "return false" : "return 2";
		string? helpMethodName = emitDtoTryParse ? null : $"PrintHelp_{cmd.RunMethodName}";

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
			sb.AppendLine($"\t\t\t\t\tPrintHelp_{cmd.RunMethodName}();");
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

		foreach (ParameterModel p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Injected)
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

			string flagKey = Escape(p.CliLongName);
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

		int posIndex = 0;
		foreach (ParameterModel p in cmd.Parameters)
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
				string fallback = p.DefaultValueLiteral ?? "default!";
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
			if (dtoOptionsType is not null)
				EmitOptionsDtoConstructionAndReturn(sb, dtoOptionsType, cmd.Parameters);
			else
				EmitAsParametersConstructionForDto(sb, cmd);

			sb.AppendLine("\t\t}");
			sb.AppendLine();
			return;
		}

		EmitAsParametersConstruction(sb, cmd);

		if (cmd.RequiresInstance)
		{
			if (cmd.ContainingTypeHasParameterlessCtor)
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
		bool useFilters = globalFilters.Length > 0 || cmd.CommandFilters.Length > 0;
		if (!useFilters)
		{
			sb.Append("\t\t\t");
			EmitInvocation(sb, cmd);
			sb.AppendLine();
		}
		else
		{
			EmitCommandPathLiteral(sb, cmd);
			sb.AppendLine("\t\t\tvar ctx = new CommandContext(commandPath, args, ct);");
			sb.AppendLine("\t\t\tCommandFilterDelegate next = async c =>");
			sb.AppendLine("\t\t\t{");
			EmitInvocation(sb, cmd, "c.CancellationToken", "c", "\t\t\t\t");
			sb.AppendLine("\t\t\t};");
			var cap = 0;
			for (int i = cmd.CommandFilters.Length - 1; i >= 0; i--)
			{
				string fq = cmd.CommandFilters[i].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				bool filterParamless = HasPublicParameterlessCtor(cmd.CommandFilters[i]);
				string name = "__cap" + cap++;
				sb.AppendLine($"\t\t\tvar {name} = next;");
				sb.AppendLine($"\t\t\tnext = async c => await {DiResolveOrNew(fq, filterParamless)}.InvokeAsync(c, {name});");
			}

			for (int i = globalFilters.Length - 1; i >= 0; i--)
			{
				string gFq = globalFilters[i].FilterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				bool gParamless = HasPublicParameterlessCtor(globalFilters[i].FilterType);
				string name = "__cap" + cap++;
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
		for (int i = 0; i < cmd.RoutePrefix.Length; i++)
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
		foreach (ParameterModel p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Injected)
				continue;

			if (p.Special == BoolSpecialKind.Bool || p.Special == BoolSpecialKind.NullableBool)
				continue;

			if (p.IsCollection && p.Kind == ParameterKind.Flag)
			{
				string elemFq = GetElementCSharpFq(p);
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

	private static void EmitBoolSwitchNames(StringBuilder sb, CommandModel cmd)
	{
		var names = new List<string>();
		var noNames = new List<string>();
		foreach (ParameterModel p in cmd.Parameters)
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
			sb.AppendLine("\t\t\tbool IsBoolSwitchNoName(string name) => false;");
			return;
		}

		sb.AppendLine("\t\t\tbool IsBoolSwitchName(string name) => name switch");
		sb.AppendLine("\t\t\t{");
		foreach (string n in names)
			sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");
		foreach (string n in noNames)
			sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");

		if (noNames.Count == 0)
		{
			sb.AppendLine("\t\t\tbool IsBoolSwitchNoName(string name) => false;");
		}
		else
		{
			sb.AppendLine("\t\t\tbool IsBoolSwitchNoName(string name) => name switch");
			sb.AppendLine("\t\t\t{");
			foreach (string n in noNames)
				sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");
			sb.AppendLine("\t\t\t\t_ => false");
			sb.AppendLine("\t\t\t};");
		}
	}

	private static void EmitCanonFlagNameMethod(StringBuilder sb, CommandModel cmd)
	{
		var cases = new List<(string from, string to)>();
		foreach (ParameterModel p in cmd.Parameters)
		{
			if (p.Kind != ParameterKind.Flag)
				continue;
			foreach (string al in p.Aliases)
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
		foreach ((string from, string to) in cases)
			sb.AppendLine($"\t\t\t\t\"{Escape(from)}\" => \"{Escape(to)}\",");

		sb.AppendLine("\t\t\t\t_ => raw");
		sb.AppendLine("\t\t\t};");
	}

	private static void EmitShortFlagMethods(StringBuilder sb, CommandModel cmd)
	{
		var shortCases = new List<(char c, string Primary, bool IsBool)>();
		foreach (ParameterModel p in cmd.Parameters)
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
		foreach ((char c, string primary, _) in shortCases)
		{
			string esc = Escape(primary);
			sb.AppendLine($"\t\t\t\t\tcase '{c}':");
			sb.AppendLine($"\t\t\t\t\t\tflags[\"{esc}\"] = val;");
			sb.AppendLine("\t\t\t\t\t\treturn true;");
		}

		sb.AppendLine("\t\t\t\t\tdefault:");
		sb.AppendLine("\t\t\t\t\t\tConsole.Error.WriteLine($\"Error: unknown short option '-{c}'.\");");
		sb.AppendLine("\t\t\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t}");

		bool anyBool = shortCases.Exists(static x => x.IsBool);
		if (!anyBool)
		{
			sb.AppendLine("\t\t\tbool IsShortBoolChar(char c) => false;");
			return;
		}

		sb.AppendLine("\t\t\tbool IsShortBoolChar(char c) => c switch");
		sb.AppendLine("\t\t\t{");
		foreach ((char c, _, bool isBool) in shortCases)
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
		string ind = "\t\t\t" + indentExtra;
		string e = Escape(p.CliLongName);
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
				sb.AppendLine($"{ind}var {targetVar} = new global::System.IO.FileInfo({rawExpr});");
			else
				sb.AppendLine($"{ind}{targetVar} = new global::System.IO.FileInfo({rawExpr});");
			return;
		}

		if (p.ScalarKind == CliScalarKind.DirectoryInfo)
		{
			if (outVarKeyword)
				sb.AppendLine($"{ind}var {targetVar} = new global::System.IO.DirectoryInfo({rawExpr});");
			else
				sb.AppendLine($"{ind}{targetVar} = new global::System.IO.DirectoryInfo({rawExpr});");
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
			sb.AppendLine($"{ind}if (!__parser.TryParse({rawExpr}, out var __pv))");
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
					string nonNull = p.IsRequired ? "!" : "";
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
		string lineIndent = "\t\t\t")
	{
		// Lambda commands: invoke through ArghApp.GetRegisteredLambda with a cast
		if (cmd.IsLambda && !string.IsNullOrEmpty(cmd.LambdaStorageKey))
		{
			EmitLambdaInvocation(sb, cmd, ctExpr, commandContextVar, lineIndent);
			return;
		}

		var args = new List<string>();
		if (cmd.HandlerMethod is null)
		{
			foreach (ParameterModel p in cmd.Parameters)
			{
				if (p.Kind == ParameterKind.Injected)
					args.Add(ctExpr);
				else
					args.Add(p.LocalVarName);
			}
		}
		else
		{
			foreach (IParameterSymbol mp in cmd.HandlerMethod.Parameters)
			{
				if (IsInjected(mp))
				{
					args.Add(ctExpr);
					continue;
				}

				if (HasAsParametersAttribute(mp))
				{
					args.Add(AsParametersConstructedVarName(mp.Name));
					continue;
				}

				foreach (ParameterModel p in cmd.Parameters)
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

		string argList = string.Join(", ", args);
		string call = cmd.RequiresInstance
			? $"__cmdHandler.{cmd.MethodName}({argList})"
			: $"{cmd.ContainingTypeFq}.{cmd.MethodName}({argList})";

		string ret0 = commandContextVar is null
			? $"{lineIndent}return 0;"
			: $"{lineIndent}{commandContextVar}.ExitCode = 0;\n{lineIndent}return;";

		INamedTypeSymbol? ret = cmd.ReturnType;
		if (ret is null)
		{
			sb.AppendLine($"{lineIndent}{call};");
			sb.AppendLine(ret0);
			return;
		}

		string retFq = ret.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		if (retFq == "global::System.Void")
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

		if (ret is INamedTypeSymbol named && named.IsGenericType &&
		    named.ConstructedFrom.Name == "Task" &&
		    named.ConstructedFrom.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
		{
			ITypeSymbol tArg = named.TypeArguments[0];
			if (tArg.SpecialType == SpecialType.System_Int32)
			{
				if (commandContextVar is null)
					sb.AppendLine($"{lineIndent}return await {call}.ConfigureAwait(false);");
				else
				{
					sb.AppendLine($"{lineIndent}{commandContextVar}.ExitCode = await {call}.ConfigureAwait(false);");
					sb.AppendLine($"{lineIndent}return;");
				}
			}
			else
			{
				sb.AppendLine($"{lineIndent}await {call}.ConfigureAwait(false);");
				sb.AppendLine(ret0);
			}

			return;
		}

		if (retFq == "global::System.Threading.Tasks.ValueTask")
		{
			sb.AppendLine($"{lineIndent}await {call}.ConfigureAwait(false);");
			sb.AppendLine(ret0);
			return;
		}

		if (ret is INamedTypeSymbol vn && vn.IsGenericType &&
		    vn.ConstructedFrom.Name == "ValueTask" &&
		    vn.ConstructedFrom.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
		{
			ITypeSymbol tArg = vn.TypeArguments[0];
			if (tArg.SpecialType == SpecialType.System_Int32)
			{
				if (commandContextVar is null)
					sb.AppendLine($"{lineIndent}return await {call}.ConfigureAwait(false);");
				else
				{
					sb.AppendLine($"{lineIndent}{commandContextVar}.ExitCode = await {call}.ConfigureAwait(false);");
					sb.AppendLine($"{lineIndent}return;");
				}
			}
			else
			{
				sb.AppendLine($"{lineIndent}await {call}.ConfigureAwait(false);");
				sb.AppendLine(ret0);
			}

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
		foreach (ParameterModel p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Injected)
				lambdaArgs.Add(ctExpr);
			else
				lambdaArgs.Add(p.LocalVarName);
		}
		string lambdaArgList = string.Join(", ", lambdaArgs);
		string castType = string.IsNullOrEmpty(cmd.LambdaDelegateFq) || cmd.LambdaDelegateFq == "global::System.Delegate"
			? "global::System.Delegate"
			: cmd.LambdaDelegateFq;

		string lambdaRet0 = commandContextVar is null
			? $"{lineIndent}return 0;"
			: $"{lineIndent}{commandContextVar}.ExitCode = 0;\n{lineIndent}return;";

		INamedTypeSymbol? lambdaRetType = cmd.ReturnType;
		bool lambdaIsTaskOfInt = lambdaRetType is INamedTypeSymbol { IsGenericType: true } lnt &&
			lnt.TypeArguments.Length == 1 && lnt.TypeArguments[0].SpecialType == SpecialType.System_Int32;

		if (castType == "global::System.Delegate")
		{
			// Fallback: use DynamicInvoke
			sb.AppendLine($"{lineIndent}var __lambdaDelegate = global::Nullean.Argh.ArghApp.GetRegisteredLambda(\"{Escape(cmd.LambdaStorageKey)}\");");
			sb.AppendLine($"{lineIndent}__lambdaDelegate?.DynamicInvoke({lambdaArgList});");
			sb.AppendLine(lambdaRet0);
		}
		else
		{
			sb.AppendLine($"{lineIndent}var __lambdaDelegate = (({castType})global::Nullean.Argh.ArghApp.GetRegisteredLambda(\"{Escape(cmd.LambdaStorageKey)}\")!);");
			string lambdaRetFq = lambdaRetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
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

		foreach (ParameterModel p in model.Members)
		{
			if (p.Kind == ParameterKind.Flag)
				yield return p;
		}
	}

	private static void AddCliKeys(IEnumerable<ParameterModel> flags, HashSet<string> keys)
	{
		foreach (ParameterModel p in flags)
		{
			keys.Add(p.CliLongName);
			foreach (string a in p.Aliases)
			{
				if (!string.IsNullOrEmpty(a))
					keys.Add(a);
			}
		}
	}

	private static List<(string Segment, OptionsTypeModel Model)> GetCommandNamespaceOptionChain(AppEmitModel app, ImmutableArray<string> routePrefix)
	{
		var list = new List<(string, OptionsTypeModel)>();
		RegistryNode current = app.Root;
		foreach (string seg in routePrefix)
		{
			RegistryNode.NamedCommandNamespaceChild? found = null;
			foreach (RegistryNode.NamedCommandNamespaceChild c in current.Children)
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

		foreach (string a in p.Aliases)
		{
			if (!string.IsNullOrEmpty(a) && scopedKeys.Contains(a))
				return true;
		}

		return false;
	}

	private static void EmitHelpOptionRows(StringBuilder sb, IReadOnlyList<ParameterModel> rows, int maxOptWidth)
	{
		foreach (ParameterModel p in rows)
		{
			string left = HelpLayout.FormatOptionLeftCell(p).PadRight(maxOptWidth);
			string desc = BuildDescriptionSuffix(p, forPositional: false);
			sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {Escape(desc)}\");");
		}
	}

	private static void EmitCommandHelpPrinter(StringBuilder sb, CommandModel cmd, AppEmitModel app, string entryAssemblyName)
	{
		string routeUsage = cmd.RoutePrefix.IsDefaultOrEmpty
			? ""
			: string.Join(" ", cmd.RoutePrefix) + " ";

		List<ParameterModel> globalFlagMembers = EnumerateFlagMembers(app.GlobalOptionsModel).ToList();
		List<(string Segment, List<ParameterModel> Rows)> namespaceOptionSections = new();
		List<(string Segment, OptionsTypeModel Model)> namespaceOptionChain = GetCommandNamespaceOptionChain(app, cmd.RoutePrefix);
		var suppressedForNamespaceDisplay = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddCliKeys(globalFlagMembers, suppressedForNamespaceDisplay);
		foreach ((string seg, OptionsTypeModel gom) in namespaceOptionChain)
		{
			List<ParameterModel> allInNamespace = EnumerateFlagMembers(gom).ToList();
			List<ParameterModel> rows = allInNamespace.Where(p => !suppressedForNamespaceDisplay.Contains(p.CliLongName)).ToList();
			AddCliKeys(allInNamespace, suppressedForNamespaceDisplay);
			if (rows.Count > 0)
				namespaceOptionSections.Add((seg, rows));
		}

		var scopedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddCliKeys(globalFlagMembers, scopedKeys);
		foreach ((_, OptionsTypeModel gom) in namespaceOptionChain)
			AddCliKeys(EnumerateFlagMembers(gom), scopedKeys);

		List<ParameterModel> commandOnlyFlags = cmd.Parameters
			.Where(p => p.Kind == ParameterKind.Flag && !CommandFlagMatchesScopedKeys(p, scopedKeys))
			.ToList();

		var widthCandidates = new List<int> { "--help, -h".Length };
		widthCandidates.AddRange(globalFlagMembers.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		foreach ((_, List<ParameterModel> rows) in namespaceOptionSections)
			widthCandidates.AddRange(rows.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));

		widthCandidates.AddRange(commandOnlyFlags.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		int maxOptWidth = Math.Min(widthCandidates.Max(), 40);
		maxOptWidth = Math.Max(maxOptWidth, "--help, -h".Length);

		sb.AppendLine($"\t\tprivate static void PrintHelp_{cmd.RunMethodName}()");
		sb.AppendLine("\t\t{");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Usage: \") + CliHelpFormatting.Accent(\"{Escape(entryAssemblyName)}\") + \" {Escape(routeUsage)}{Escape(cmd.CommandName)} {Escape(cmd.UsageHints)}\");");

		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");

		string descToShow = string.IsNullOrWhiteSpace(cmd.RemarksRendered) ? cmd.SummaryOneLiner : cmd.RemarksRendered;
		if (!string.IsNullOrWhiteSpace(descToShow))
		{
			foreach (string line in descToShow.Split('\n'))
			{
				string trimmed = line.TrimEnd('\r');
				if (trimmed.Length == 0)
					sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
				else
					sb.AppendLine($"\t\t\tConsole.Out.WriteLine(\"{Escape(trimmed)}\");");
			}

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		bool hasArgs = false;
		foreach (ParameterModel p in cmd.Parameters)
		{
			if (p.Kind == ParameterKind.Positional)
				hasArgs = true;
		}

		if (hasArgs)
		{
			int maxArgWidth = cmd.Parameters
				.Where(p => p.Kind == ParameterKind.Positional)
				.Select(p => (p.IsRequired ? $"<{p.CliLongName}>" : $"[<{p.CliLongName}>]").Length)
				.DefaultIfEmpty(0).Max();
			maxArgWidth = Math.Min(maxArgWidth, 40);

			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Arguments:\"));");
			foreach (ParameterModel p in cmd.Parameters)
			{
				if (p.Kind != ParameterKind.Positional)
					continue;

				string nameCell = p.IsRequired
					? $"<{p.CliLongName}>"
					: $"[<{p.CliLongName}>]";
				string nameCellPadded = nameCell.PadRight(maxArgWidth);
				string desc = BuildDescriptionSuffix(p, forPositional: true);
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Placeholder(\"{Escape(nameCellPadded)}\")}}  {Escape(desc)}\");");
			}

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Global options:\"));");
		EmitHelpOptionRows(sb, globalFlagMembers, maxOptWidth);
		string globalHelpLeft = "--help, -h".PadRight(maxOptWidth);
		sb.AppendLine($"\t\t\tConsole.Out.WriteLine(\"  \" + CliHelpFormatting.Accent(\"{Escape(globalHelpLeft)}\") + \"  Show help.\");");
		sb.AppendLine("\t\t\tConsole.Out.WriteLine();");

		foreach ((string segment, List<ParameterModel> gRows) in namespaceOptionSections)
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

		if (!string.IsNullOrWhiteSpace(cmd.ExamplesRendered))
		{
			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Examples:\"));");
			foreach (string line in cmd.ExamplesRendered.Split('\n'))
			{
				string trimmed = line.TrimEnd('\r');
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
				foreach (string member in p.EnumMemberNames)
				{
					if (docs.TryGetValue(member, out string? memberDoc) && !string.IsNullOrWhiteSpace(memberDoc))
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

	private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

	private sealed record CommandModel(
		ImmutableArray<string> RoutePrefix,
		string CommandName,
		string RunMethodName,
		string ContainingTypeFq,
		string MethodName,
		bool RequiresInstance,
		bool ContainingTypeHasParameterlessCtor,
		INamedTypeSymbol? ReturnType,
		ImmutableArray<ParameterModel> Parameters,
		IMethodSymbol? HandlerMethod,
		string SummaryOneLiner,
		string RemarksRendered,
		string ExamplesRendered,
		string UsageHints,
		ImmutableArray<INamedTypeSymbol> CommandFilters,
		bool IsLambda = false,
		string LambdaStorageKey = "",
		string LambdaDelegateFq = "")
	{
		public static CommandModel FromMethod(
			string commandName,
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			ImmutableArray<string> routePrefix,
			SourceProductionContext context,
			Location diagnosticLocation)
		{
			ImmutableArray<ParameterModel> parameters = BuildParameterModels(method, parseOptions, context, diagnosticLocation);
			ReportDuplicateCliNames(context, diagnosticLocation, parameters);
			ValidateExpandedParameterLayout(context, diagnosticLocation, parameters);
			foreach (ParameterModel p in parameters)
			{
				if (p.IsCollection && p.Kind == ParameterKind.Positional)
					context.ReportDiagnostic(Diagnostic.Create(CollectionPositionalNotSupported, diagnosticLocation));
			}

			MethodDocumentation docs = Documentation.ParseMethod(method.GetDocumentationCommentXml(), parseOptions);
			ImmutableArray<ParameterModel> withDocs = ApplyParamDocumentation(parameters, method, docs.ParamDocsRaw);
			withDocs = ApplyCollectionSeparatorsFromDocumentation(withDocs, method, docs.ParamSeparators);
			string usage = UsageSynopsis.Build(withDocs);
			string runName = BuildRunMethodName(routePrefix, commandName);
			string containingFq = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			ImmutableArray<INamedTypeSymbol> cmdFilters = CollectCommandFilters(method);
			bool hasParamlessCtor = method.ContainingType is INamedTypeSymbol namedCt &&
			                        HasPublicParameterlessCtor(namedCt);
			return new CommandModel(
				routePrefix,
				commandName,
				runName,
				containingFq,
				method.Name,
				!method.IsStatic,
				hasParamlessCtor,
				method.ReturnType as INamedTypeSymbol,
				withDocs,
				method,
				docs.SummaryOneLiner,
				docs.RemarksRendered,
				docs.ExamplesRendered,
				usage,
				cmdFilters);
		}

		private static ImmutableArray<ParameterModel> BuildParameterModels(
			IMethodSymbol method,
			CSharpParseOptions parseOptions,
			SourceProductionContext context,
			Location diagnosticLocation)
		{
			var builder = ImmutableArray.CreateBuilder<ParameterModel>();
			foreach (IParameterSymbol p in method.Parameters)
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

					string? prefix = GetAsParametersPrefix(p);
					foreach (ParameterModel pm in FlattenAsParametersType(context, diagnosticLocation, p, namedType, prefix, parseOptions))
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
			foreach (ParameterModel p in parameters)
			{
				if (!p.IsCollection || p.CollectionSeparator is not null)
				{
					b.Add(p);
					continue;
				}

				if (paramSeparators.TryGetValue(p.SymbolName, out string? sep) && !string.IsNullOrWhiteSpace(sep))
					b.Add(p with { CollectionSeparator = sep });
				else
					b.Add(p);
			}

			return b.ToImmutable();
		}

		private static ImmutableArray<INamedTypeSymbol> CollectCommandFilters(IMethodSymbol method)
		{
			var b = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
			foreach (AttributeData attr in method.GetAttributes())
			{
				INamedTypeSymbol? ac = attr.AttributeClass;
				if (ac is null || ac.Name != "FilterAttribute" || ac.TypeArguments.Length != 1)
					continue;
				if (ac.TypeArguments[0] is INamedTypeSymbol ft && ft.TypeKind != TypeKind.Error)
					b.Add(ft);
			}

			return b.ToImmutable();
		}

		private static string BuildRunMethodName(ImmutableArray<string> routePrefix, string commandName)
		{
			if (routePrefix.IsDefaultOrEmpty)
				return "Run_" + Naming.SanitizeIdentifier(commandName);

			var sb = new StringBuilder();
			sb.Append("Run");
			foreach (string seg in routePrefix)
			{
				sb.Append('_');
				sb.Append(Naming.SanitizeIdentifier(seg));
			}

			sb.Append('_');
			sb.Append(Naming.SanitizeIdentifier(commandName));
			return sb.ToString();
		}

		private static ImmutableArray<ParameterModel> ApplyParamDocumentation(
			ImmutableArray<ParameterModel> parameters,
			IMethodSymbol method,
			ImmutableDictionary<string, string> paramDocsRaw)
		{
			if (paramDocsRaw.IsEmpty)
				return parameters;

			var map = new Dictionary<string, ParameterModel>();
			foreach (ParameterModel p in parameters)
				map[p.SymbolName] = p;

			foreach (IParameterSymbol ps in method.Parameters)
			{
				if (!map.TryGetValue(ps.Name, out ParameterModel existing))
					continue;
				if (!paramDocsRaw.TryGetValue(ps.Name, out string? raw) || string.IsNullOrWhiteSpace(raw))
					continue;

				if (existing.Kind == ParameterKind.Positional)
				{
					map[ps.Name] = existing with { Description = raw.Trim() };
					continue;
				}

				ParamDoc doc = ParamDocParser.Parse(raw);
				map[ps.Name] = existing with
				{
					Description = doc.Description,
					ShortOpt = doc.ShortOpt,
					Aliases = doc.Aliases
				};
			}

			var rebuilt = ImmutableArray.CreateBuilder<ParameterModel>(parameters.Length);
			foreach (ParameterModel p in parameters)
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
		public static ParameterModel From(IParameterSymbol p)
		{
			bool isArg = HasArgumentAttribute(p);

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

			ParameterKind kind = isArg ? ParameterKind.Positional : ParameterKind.Flag;
			BoolSpecialKind bs = ClassifyBool(p.Type);
			if (TryUnwrapCollectionType(p.Type, out ITypeSymbol? elemType) && bs == BoolSpecialKind.None)
				return FromCollectionParameter(p, elemType, kind);

			ClassifyScalar(p, bs, out CliScalarKind sk, out string typeName, out string? enumFq, out ImmutableArray<string> enumMembers, out string? parserFq, out string? customValFq);
			bool required = ComputeRequired(p, bs);
			string? defLit = TryGetDefaultLiteral(p, bs);
			ImmutableDictionary<string, string>? enumDocs = null;
			if (sk == CliScalarKind.Enum)
			{
				ITypeSymbol et = p.Type;
				if (et is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nul)
					et = nul.TypeArguments[0];
				if (et is INamedTypeSymbol en)
					enumDocs = GetEnumMemberDocs(en);
			}
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
			BoolSpecialKind bs = ClassifyBool(prop.Type);
			if (TryUnwrapCollectionType(prop.Type, out ITypeSymbol? elemType) && bs == BoolSpecialKind.None)
				return FromOptionsCollection(prop, elemType);

			ClassifyScalarForType(prop.Type, prop, bs, out CliScalarKind sk, out string typeName, out string? enumFq,
				out ImmutableArray<string> enumMembers, out string? parserFq, out string? customValFq);
			bool required = ComputeRequiredForOptionsType(prop.Type, bs);
			ImmutableDictionary<string, string>? enumDocs = null;
			if (sk == CliScalarKind.Enum)
			{
				ITypeSymbol et = prop.Type;
				if (et is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nul)
					et = nul.TypeArguments[0];
				if (et is INamedTypeSymbol en)
					enumDocs = GetEnumMemberDocs(en);
			}
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
			BoolSpecialKind bs = ClassifyBool(field.Type);
			if (TryUnwrapCollectionType(field.Type, out ITypeSymbol? elemType) && bs == BoolSpecialKind.None)
			{
				// fields use same shape as properties for collections
				ClassifyScalarForType(elemType, field, BoolSpecialKind.None, out CliScalarKind elemSk, out string elemTn,
					out string? eFq, out ImmutableArray<string> eMem, out string? pFq, out string? cFq);
				string? sep = TryGetCollectionSeparatorFromAttribute(field);
				bool fieldCollRequired = ComputeRequiredForOptionsType(field.Type, BoolSpecialKind.None);
				string fq = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				return new ParameterModel(
					field.Name,
					SafeLocalName(field.Name),
					Naming.ToCliLongName(field.Name),
					ParameterKind.Flag,
					BoolSpecialKind.None,
					CliScalarKind.Collection,
					"values",
					null,
					ImmutableArray<string>.Empty,
					null,
					null,
					fieldCollRequired,
					null,
					"",
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
					CollectionTargetIsArray: field.Type is IArrayTypeSymbol);
			}

			ClassifyScalarForType(field.Type, field, bs, out CliScalarKind sk, out string typeName, out string? enumFq,
				out ImmutableArray<string> enumMembers, out string? parserFq, out string? customValFq);
			bool required = ComputeRequiredForOptionsType(field.Type, bs);
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

		private static ParameterModel FromCollectionParameter(IParameterSymbol p, ITypeSymbol elementType, ParameterKind kind)
		{
			ClassifyScalarForType(elementType, p, BoolSpecialKind.None, out CliScalarKind elemSk, out string elemTn,
				out string? eFq, out ImmutableArray<string> eMem, out string? pFq, out string? cFq);
			string? sep = TryGetCollectionSeparatorFromAttribute(p);
			bool required = ComputeRequired(p, BoolSpecialKind.None);
			string? defLit = TryGetDefaultLiteral(p, BoolSpecialKind.None);
			string fq = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			return new ParameterModel(
				p.Name,
				SafeLocalName(p.Name),
				Naming.ToCliLongName(p.Name),
				kind,
				BoolSpecialKind.None,
				CliScalarKind.Collection,
				"values",
				null,
				ImmutableArray<string>.Empty,
				null,
				null,
				required,
				defLit,
				"",
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
				CollectionTargetIsArray: p.Type is IArrayTypeSymbol);
		}

		private static ParameterModel FromOptionsCollection(IPropertySymbol prop, ITypeSymbol elementType)
		{
			ClassifyScalarForType(elementType, prop, BoolSpecialKind.None, out CliScalarKind elemSk, out string elemTn,
				out string? eFq, out ImmutableArray<string> eMem, out string? pFq, out string? cFq);
			string? sep = TryGetCollectionSeparatorFromAttribute(prop);
			bool required = ComputeRequiredForOptionsType(prop.Type, BoolSpecialKind.None);
			string fq = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			return new ParameterModel(
				prop.Name,
				SafeLocalName(prop.Name),
				Naming.ToCliLongName(prop.Name),
				ParameterKind.Flag,
				BoolSpecialKind.None,
				CliScalarKind.Collection,
				"values",
				null,
				ImmutableArray<string>.Empty,
				null,
				null,
				required,
				null,
				"",
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
				CollectionTargetIsArray: prop.Type is IArrayTypeSymbol);
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
			bool isArg = HasArgumentAttribute(cp);
			ParameterKind kind = isArg ? ParameterKind.Positional : ParameterKind.Flag;
			BoolSpecialKind bs = ClassifyBool(cp.Type);
			string cli = namePrefix + Naming.ToCliLongName(cp.Name);
			string local = SafeLocalName(methodParamName + "_" + cp.Name);
			string desc = Documentation.GetParamDocFromType(containingType, cp.Name);
			if (TryUnwrapCollectionType(cp.Type, out ITypeSymbol? elemType) && bs == BoolSpecialKind.None)
			{
				ClassifyScalarForType(elemType, cp, BoolSpecialKind.None, out CliScalarKind elemSk, out string elemTn,
					out string? eFq, out ImmutableArray<string> eMem, out string? pFq, out string? cFq);
				string? sep = TryGetCollectionSeparatorFromAttribute(cp);
				bool cpCollRequired = ComputeRequired(cp, BoolSpecialKind.None);
				string? cpCollDefLit = TryGetDefaultLiteral(cp, BoolSpecialKind.None);
				string fullFq = cp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				return new ParameterModel(
					cp.Name,
					local,
					cli,
					kind,
					BoolSpecialKind.None,
					CliScalarKind.Collection,
					"values",
					null,
					ImmutableArray<string>.Empty,
					null,
					null,
					cpCollRequired,
					cpCollDefLit,
					desc,
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
					FullDeclaredTypeFq: fullFq,
					CollectionTargetIsArray: cp.Type is IArrayTypeSymbol,
					AsParametersOwnerParamName: methodParamName,
					AsParametersMemberOrder: memberOrder,
					AsParametersTypeFq: typeFq,
					AsParametersUseInit: false,
					AsParametersClrName: cp.Name);
			}

			ClassifyScalar(cp, bs, out CliScalarKind sk, out string typeName, out string? enumFq, out ImmutableArray<string> enumMembers, out string? parserFq, out string? customValFq);
			bool required = ComputeRequired(cp, bs);
			string? defLit = TryGetDefaultLiteral(cp, bs);
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
			bool isArg = HasArgumentAttribute(prop);
			ParameterKind kind = isArg ? ParameterKind.Positional : ParameterKind.Flag;
			BoolSpecialKind bs = ClassifyBool(prop.Type);
			string cli = namePrefix + Naming.ToCliLongName(prop.Name);
			string local = SafeLocalName(methodParamName + "_" + prop.Name);
			string desc = Documentation.GetPropertySummaryLine(prop);
			if (TryUnwrapCollectionType(prop.Type, out ITypeSymbol? elemType) && bs == BoolSpecialKind.None)
			{
				ClassifyScalarForType(elemType, prop, BoolSpecialKind.None, out CliScalarKind elemSk, out string elemTn,
					out string? eFq, out ImmutableArray<string> eMem, out string? pFq, out string? cFq);
				string? sep = TryGetCollectionSeparatorFromAttribute(prop);
				bool propCollRequired = ComputeRequiredForOptionsType(prop.Type, BoolSpecialKind.None);
				string? propCollDefLit = null;
				string fullFq = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				return new ParameterModel(
					prop.Name,
					local,
					cli,
					kind,
					BoolSpecialKind.None,
					CliScalarKind.Collection,
					"values",
					null,
					ImmutableArray<string>.Empty,
					null,
					null,
					propCollRequired,
					propCollDefLit,
					desc,
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
					FullDeclaredTypeFq: fullFq,
					CollectionTargetIsArray: prop.Type is IArrayTypeSymbol,
					AsParametersOwnerParamName: methodParamName,
					AsParametersMemberOrder: memberOrder,
					AsParametersTypeFq: typeFq,
					AsParametersUseInit: true,
					AsParametersClrName: prop.Name);
			}

			ClassifyScalarForType(prop.Type, prop, bs, out CliScalarKind sk, out string typeName, out string? enumFq,
				out ImmutableArray<string> enumMembers, out string? parserFq, out string? customValFq);
			bool required = ComputeRequiredForOptionsType(prop.Type, bs);
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

			ITypeSymbol t = type;
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
				string fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
			foreach (AttributeData attr in symbol.GetAttributes())
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

			ITypeSymbol t = p.Type;
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
				string fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
			foreach (ISymbol m in enumType.GetMembers())
			{
				if (m is IFieldSymbol { HasConstantValue: true, IsImplicitlyDeclared: false })
					b.Add(m.Name);
			}

			return b.ToImmutable();
		}

		private static ImmutableDictionary<string, string> GetEnumMemberDocs(INamedTypeSymbol enumType)
		{
			var b = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
			foreach (ISymbol m in enumType.GetMembers())
			{
				if (m is not IFieldSymbol { HasConstantValue: true, IsImplicitlyDeclared: false } field)
					continue;
				string? xml = field.GetDocumentationCommentXml();
				if (string.IsNullOrWhiteSpace(xml))
					continue;
				try
				{
					var doc = System.Xml.Linq.XDocument.Parse("<root>" + xml + "</root>", System.Xml.Linq.LoadOptions.PreserveWhitespace);
					string summary = Documentation.FlattenBlockPublic(doc.Root?.Element("summary")).Replace("\r\n", "\n").Trim();
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

			string fq = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
				string inner = GetSimpleTypeName(nn.TypeArguments[0]);
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

			object? v = p.ExplicitDefaultValue;
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
			string k = Naming.ToCliLongName(name).Replace("-", "_");
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
		Injected
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
			foreach (char c in commandName)
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
			foreach (string s in suffixes)
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
			for (int i = 0; i < name.Length; i++)
			{
				char c = name[i];
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

			string[] parts = text.Split(',');
			char? shortOpt = null;
			var aliases = ImmutableArray.CreateBuilder<string>();
			int i = 0;
			for (; i < parts.Length; i++)
			{
				string seg = parts[i].Trim();
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

			string desc = i >= parts.Length ? "" : string.Join(",", parts, i, parts.Length - i).Trim();
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

			string th = TypeHint(p);
			var sb = new StringBuilder();
			if (p.ShortOpt is char ch)
			{
				sb.Append('-').Append(ch).Append(", ");
			}

			foreach (string a in p.Aliases)
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
		ImmutableDictionary<string, string> ParamDocsRaw,
		ImmutableDictionary<string, string> ParamSeparators);

	private static class Documentation
	{
		public static MethodDocumentation ParseMethod(string? xml, CSharpParseOptions parseOptions)
		{
			if (string.IsNullOrWhiteSpace(xml))
				return new MethodDocumentation("", "", "", ImmutableDictionary<string, string>.Empty,
					ImmutableDictionary<string, string>.Empty);

			try
			{
				var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
				XElement? root = doc.Root;
				if (root is null)
					return new MethodDocumentation("", "", "", ImmutableDictionary<string, string>.Empty,
						ImmutableDictionary<string, string>.Empty);

				string summary = FlattenBlock(root.Element("summary")).Replace("\r\n", "\n").Trim();
				string remarks = FlattenBlock(root.Element("remarks")).Replace("\r\n", "\n").Trim();
				string examples = string.Join("\n\n", root.Elements("example")
					.Select(e => FlattenBlock(e).Replace("\r\n", "\n").Trim())
					.Where(s => !string.IsNullOrWhiteSpace(s)));
				ImmutableDictionary<string, string>.Builder paramMap =
					ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
				ImmutableDictionary<string, string>.Builder sepMap =
					ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
				foreach (XElement pe in root.Elements("param"))
				{
					string? name = pe.Attribute("name")?.Value;
					if (string.IsNullOrEmpty(name))
						continue;

					XElement? sepEl = pe.Elements().FirstOrDefault(e => e.Name.LocalName == "separator");
					if (sepEl is not null && !string.IsNullOrEmpty(sepEl.Value))
						sepMap[name!] = sepEl.Value.Trim();

					paramMap[name!] = FlattenParam(pe);
				}

				return new MethodDocumentation(summary, remarks, examples, paramMap.ToImmutable(), sepMap.ToImmutable());
			}
			catch
			{
				return new MethodDocumentation("", "", "", ImmutableDictionary<string, string>.Empty,
					ImmutableDictionary<string, string>.Empty);
			}
		}

		public static string GetParamDocFromType(INamedTypeSymbol type, string parameterName)
		{
			string? xml = type.GetDocumentationCommentXml();
			if (string.IsNullOrWhiteSpace(xml))
				return "";
			try
			{
				var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
				XElement? root = doc.Root;
				if (root is null)
					return "";
				foreach (XElement pe in root.Elements("param"))
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
			string? xml = prop.GetDocumentationCommentXml();
			if (string.IsNullOrWhiteSpace(xml))
				return "";
			try
			{
				var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
				XElement? root = doc.Root;
				if (root is null)
					return "";
				return FlattenBlock(root.Element("summary")).Replace("\r\n", "\n").Trim();
			}
			catch
			{
				return "";
			}
		}

		private static string FlattenParam(XElement param)
		{
			var sb = new StringBuilder();
			foreach (XNode n in param.Nodes())
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
			foreach (XNode n in nodes)
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
						sb.AppendLine();
						break;
					case XElement e when e.Name.LocalName == "code":
						sb.AppendLine();
						foreach (XNode c in e.Nodes())
						{
							if (c is XText tx)
								sb.Append("    ").AppendLine(tx.Value.TrimEnd());
						}

						break;
					case XElement e when e.Name.LocalName == "list":
						if (sb.Length > 0)
							sb.AppendLine();
						foreach (XElement item in e.Elements().Where(x => x.Name.LocalName == "item"))
						{
							sb.Append("  - ");
							XElement? desc = item.Element("description");
							if (desc is not null)
								FlattenNodes(desc.Nodes(), sb);
							else
								FlattenNodes(item.Nodes(), sb);
							sb.AppendLine();
						}

						break;
					case XElement e when e.Name.LocalName == "see":
					{
						string? c = e.Attribute("cref")?.Value;
						if (c is not null)
						{
							int dot = c.LastIndexOf('.');
							sb.Append(dot >= 0 ? c.Substring(dot + 1) : c);
						}

						break;
					}
					case XElement e:
						FlattenNodes(e.Nodes(), sb);
						break;
				}
			}
		}
	}

	private static class UsageSynopsis
	{
		/// <summary>Minimal usage tail: required flags and positionals explicitly; optional switches and flags fold into a single <c>[options]</c>.</summary>
		public static string Build(ImmutableArray<ParameterModel> parameters)
		{
			var parts = new List<string>();
			bool needsOptions = false;

			foreach (ParameterModel p in parameters)
			{
				if (p.Kind == ParameterKind.Injected)
					continue;

				if (p.Kind == ParameterKind.Positional)
				{
					string seg = p.IsRequired ? $"<{p.CliLongName}>" : $"[<{p.CliLongName}>]";
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
						string typeHint = HelpLayout.TypeHint(p);
						parts.Add($"--{p.CliLongName} {typeHint}");
					}
					else
					{
						needsOptions = true;
					}

					continue;
				}

				string typeHintScalar = HelpLayout.TypeHint(p);
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
