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

	private static void EmitRootCommandHelpOverview(
		StringBuilder sb,
		CommandModel rootCmd,
		string indent,
		AppEmitModel app,
		string entryAssemblyName,
		bool includeScopedDefaultHelpOptions = true)
	{
		// "(default command)" is not an argv token — labels the opt-in default handler; summary/remarks from XML on the handler.
		sb.AppendLine($"{indent}Console.Out.WriteLine(\" \" + CliHelpFormatting.DefaultCommandLabel(\"(default command)\"));");
		EmitRootDefaultDocumentationLines(sb, indent, rootCmd.SummaryInnerXml, rootCmd.SummaryOneLiner, false);
		var remarksXml = TransformRemarksInnerXmlForHelp(rootCmd.RemarksInnerXml, rootCmd, app.AllCommands, entryAssemblyName);
		EmitRootDefaultDocumentationLines(sb, indent, remarksXml, rootCmd.RemarksRendered, true);
		var rootFlags = rootCmd.Parameters.Where(static p => p.Kind == ParameterKind.Flag).ToList();
		if (includeScopedDefaultHelpOptions && rootFlags.Count > 0)
		{
			var mw = Math.Min(
				Math.Max(rootFlags.Max(p => HelpLayout.FormatOptionLeftCell(p).Length), "-h, --help".Length),
				40);
			sb.AppendLine($"{indent}Console.Out.WriteLine();");
			sb.AppendLine($"{indent}Console.Out.WriteLine(CliHelpFormatting.Section(\"Options for this default:\"));");
			foreach (var p in rootFlags)
			{
				var left = HelpLayout.FormatOptionLeftCell(p).PadRight(mw);
				var desc = BuildDescriptionSuffix(p, forPositional: false);
				sb.AppendLine(
					$"{indent}Console.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {EscapeForHelpInterpolation(desc)}\");");
			}
		}

		sb.AppendLine($"{indent}Console.Out.WriteLine();");
	}

	private static void EmitRootAliasHelpOverview(StringBuilder sb, CommandModel aliasCmd, ImmutableArray<string> path, string indent, string entryAssemblyName)
	{
		var fullCommandPath = path.IsDefaultOrEmpty
			? $"{entryAssemblyName} {aliasCmd.CommandName}"
			: $"{entryAssemblyName} {string.Join(" ", path)} {aliasCmd.CommandName}";
		sb.AppendLine($"{indent}Console.Out.WriteLine(\" \" + CliHelpFormatting.DefaultCommandLabel(\"(default: {Escape(aliasCmd.CommandName)})\"));");
		if (!string.IsNullOrWhiteSpace(aliasCmd.SummaryOneLiner))
			sb.AppendLine($"{indent}Console.Out.WriteLine(\"    {Escape(aliasCmd.SummaryOneLiner)}\");");
		sb.AppendLine($"{indent}Console.Out.WriteLine(\"    Alias for '{Escape(fullCommandPath)}'. Run '{Escape(fullCommandPath)} --help' for details.\");");
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


	private static void EmitIsIntrinsicCommand(StringBuilder sb, AppEmitModel app)
	{
		sb.AppendLine("\t\tinternal static bool IsIntrinsicCommand(string[] args)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tif (args is null || args.Length == 0) return false;");

		var intrinsicCommands = app.AllCommands.Where(static c => c.IsIntrinsic).ToList();
		if (intrinsicCommands.Count == 0)
		{
			sb.AppendLine("\t\t\treturn false;");
			sb.AppendLine("\t\t}");
			sb.AppendLine();
			return;
		}

		sb.AppendLine("\t\t\tvar match = Route(args);");
		sb.AppendLine("\t\t\tif (match is null) return false;");
		sb.AppendLine("\t\t\tswitch (match.Value.CommandPath)");
		sb.AppendLine("\t\t\t{");
		foreach (var cmd in intrinsicCommands)
		{
			var routePath = Escape(GetCommandRoutePath(cmd));
			sb.AppendLine($"\t\t\t\tcase \"{routePath}\": return true;");
		}
		sb.AppendLine("\t\t\t\tdefault: return false;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	private static void EmitTryParseRouteHierarchical(StringBuilder sb, AppEmitModel app)
	{
		var hasLeadingOptionPrefetch =
			CollectRootPrefetchGlobalMembers(app).Length > 0 || CollectDeferredRootAliasPrefetchFlags(app).Length > 0;
		sb.AppendLine("\t\tpublic static bool TryParseRoute(string[] args, out RouteMatch match)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tmatch = default;");
		sb.AppendLine("\t\t\tif (CompletionProtocol.IsArghMetaCompletionInvocation(args)) return false;");
		sb.AppendLine("\t\t\tvar idx = new int[1];");
		if (hasLeadingOptionPrefetch)
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
		else if (app.Root.RootAlias is { } routeRa)
		{
			var rp = Escape(GetCommandRoutePath(routeRa));
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
		else if (node.RootAlias is { } routeNsRa)
		{
			var rnp = Escape(GetCommandRoutePath(routeNsRa));
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

	private static ImmutableArray<ParameterModel> CollectRootPrefetchGlobalMembers(AppEmitModel app) =>
		app.GlobalOptionsModel?.FlattenedMembers ?? ImmutableArray<ParameterModel>.Empty;

	/// <summary>
	/// Union of prefetch parse sets (flattened globals + flattened root-alias-command flags).
	/// Used to decide which leading flags can be peeled in <see cref="TryParseGlobalOptions"/> vs deferred to dispatch.
	/// </summary>
	private static ImmutableArray<ParameterModel> MergeRootPrefetchPredicateMembers(AppEmitModel app)
	{
		var globalFlattened = app.GlobalOptionsModel?.FlattenedMembers ?? ImmutableArray<ParameterModel>.Empty;
		if (app.Root.RootAlias is not { } aliasCmd || aliasCmd.Parameters.IsDefaultOrEmpty)
			return globalFlattened;

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var m in globalFlattened)
			if (ParticipatesInOptionPrefetch(m.Kind))
				seen.Add(m.CliLongName);

		var trailing = ImmutableArray.CreateBuilder<ParameterModel>();
		foreach (var p in aliasCmd.Parameters)
		{
			if (!ParticipatesInOptionPrefetch(p.Kind))
				continue;

			if (!seen.Add(p.CliLongName))
				continue;

			trailing.Add(p);
		}

		if (trailing.Count == 0)
			return globalFlattened;

		var merged = ImmutableArray.CreateBuilder<ParameterModel>(globalFlattened.Length + trailing.Count);
		merged.AddRange(globalFlattened);
		merged.AddRange(trailing);

		return merged.ToImmutable();
	}

	/// <summary>
	/// Root-alias-command flags minus anything already declared on <see cref="AppEmitModel.GlobalOptionsModel"/>.
	/// When these appear as leading <c>-</c>-prefixed tokens, <see cref="TryParseGlobalOptions"/> must defer them
	/// (break without consuming idx) so <c>DispatchRoot</c> can route them to <see cref="RegistryNode.RootAlias"/>.
	/// </summary>
	private static ImmutableArray<ParameterModel> CollectDeferredRootAliasPrefetchFlags(AppEmitModel app)
	{
		var merged = MergeRootPrefetchPredicateMembers(app);
		var globals = CollectRootPrefetchGlobalMembers(app);
		if (app.Root.RootAlias is null || merged.Length <= globals.Length)
			return ImmutableArray<ParameterModel>.Empty;

		var defer = ImmutableArray.CreateBuilder<ParameterModel>();
		for (var i = globals.Length; i < merged.Length; i++)
		{
			var p = merged[i];
			if (ParticipatesInOptionPrefetch(p.Kind))
				defer.Add(p);
		}

		return defer.Count == 0 ? ImmutableArray<ParameterModel>.Empty : defer.ToImmutable();
	}

	private static bool ParticipatesInOptionPrefetch(ParameterKind kind) =>
		kind == ParameterKind.Flag || kind == ParameterKind.OptionsInjected;

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
		var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var p in members)
		{
			if (p.Kind != ParameterKind.Flag)
				continue;
			allowed.Add(p.CliLongName);
			foreach (var al in p.Aliases)
			{
				if (string.Equals(al, p.CliLongName, StringComparison.OrdinalIgnoreCase))
					continue;
				allowed.Add(al);
			}

			if (p.Special == BoolSpecialKind.NullableBool)
			{
				allowed.Add("no-" + p.CliLongName);
				foreach (var al in p.Aliases)
				{
					if (string.Equals(al, p.CliLongName, StringComparison.OrdinalIgnoreCase))
						continue;
					allowed.Add("no-" + al);
				}
			}
		}

		sb.AppendLine("\t\t\tbool IsAllowedFlag(string name) => name switch");
		sb.AppendLine("\t\t\t{");
		foreach (var n in allowed.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
			sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");
	}

	private static void EmitDeferLeadingRootAliasHelpers(StringBuilder sb, ImmutableArray<ParameterModel> defer)
	{
		if (defer.IsDefaultOrEmpty)
		{
			sb.AppendLine("\t\t\tbool ShouldDeferLeadingRootAliasCanon(string name) => false;");
			sb.AppendLine("\t\t\tbool ShouldDeferLeadingShortFlag(char c) => false;");
			return;
		}

		var canonNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var shortChars = new HashSet<char>();
		foreach (var p in defer)
		{
			canonNames.Add(p.CliLongName);
			foreach (var al in p.Aliases)
			{
				if (string.Equals(al, p.CliLongName, StringComparison.OrdinalIgnoreCase))
					continue;
				canonNames.Add(al);
			}

			if (p.Special == BoolSpecialKind.NullableBool)
			{
				canonNames.Add("no-" + p.CliLongName);
				foreach (var al in p.Aliases)
				{
					if (string.Equals(al, p.CliLongName, StringComparison.OrdinalIgnoreCase))
						continue;
					canonNames.Add("no-" + al);
				}
			}

			if (p.ShortOpt is char ch)
				shortChars.Add(ch);
		}

		sb.AppendLine("\t\t\tbool ShouldDeferLeadingRootAliasCanon(string name) => name switch");
		sb.AppendLine("\t\t\t{");
		foreach (var n in canonNames.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
			sb.AppendLine($"\t\t\t\t\"{Escape(n)}\" => true,");

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");

		if (shortChars.Count == 0)
		{
			sb.AppendLine("\t\t\tbool ShouldDeferLeadingShortFlag(char c) => false;");
			return;
		}

		sb.AppendLine("\t\t\tbool ShouldDeferLeadingShortFlag(char c) => c switch");
		sb.AppendLine("\t\t\t{");
		foreach (var ch in shortChars.OrderBy(static x => x))
			sb.AppendLine($"\t\t\t\t'{ch}' => true,");

		sb.AppendLine("\t\t\t\t_ => false");
		sb.AppendLine("\t\t\t};");
	}

	private static bool HelpUsesEnumChoiceContinuationLayout(ParameterModel p) =>
		p.ScalarKind == CliScalarKind.Enum
		|| (p.IsCollection && p.ElementScalarKind == CliScalarKind.Enum && !p.ElementEnumMemberNames.IsDefaultOrEmpty);

	private static void EmitHelpOptionRows(StringBuilder sb, IReadOnlyList<ParameterModel> rows, int maxOptWidth)
	{
		var continuationIndent = new string(' ', maxOptWidth + 4);
		foreach (var p in rows)
		{
			var left = HelpLayout.FormatOptionLeftCell(p).PadRight(maxOptWidth);
			var desc = BuildDescriptionSuffix(p, forPositional: false);
			var validationLine = BuildValidationLine(p);
			var validationOnNewLine = validationLine != null && HelpUsesEnumChoiceContinuationLayout(p);

			if (validationLine is null)
			{
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {EscapeForHelpInterpolation(desc)}\");");
			}
			else if (validationOnNewLine)
			{
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {EscapeForHelpInterpolation(desc)}\");");
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"{continuationIndent}{{CliHelpFormatting.DocRemarksLine(\"{Escape(validationLine)}\")}}\");");
			}
			else if (string.IsNullOrEmpty(desc))
			{
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {{CliHelpFormatting.DocRemarksLine(\"{Escape(validationLine)}\")}}\");");
			}
			else
			{
				sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {EscapeForHelpInterpolation(desc)} {{CliHelpFormatting.DocRemarksLine(\"{Escape(validationLine)}\")}}\");");
			}
		}
	}

	/// <summary>Same layout as <see cref="EmitHelpOptionRows"/> but to stderr (parse errors).</summary>
	private static void EmitHelpOptionRowsStdErr(StringBuilder sb, ParameterModel p, int maxOptWidth, string lineIndent)
	{
		var continuationIndent = new string(' ', maxOptWidth + 4);
		var left = HelpLayout.FormatOptionLeftCell(p).PadRight(maxOptWidth);
		var desc = BuildDescriptionSuffix(p, forPositional: false);
		var validationLine = BuildValidationLine(p);
		var validationOnNewLine = validationLine != null && HelpUsesEnumChoiceContinuationLayout(p);

		if (validationLine is null)
		{
			sb.AppendLine(
				$"{lineIndent}Console.Error.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {EscapeForHelpInterpolation(desc)}\");");
		}
		else if (validationOnNewLine)
		{
			sb.AppendLine(
				$"{lineIndent}Console.Error.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {EscapeForHelpInterpolation(desc)}\");");
			sb.AppendLine(
				$"{lineIndent}Console.Error.WriteLine($\"{continuationIndent}{{CliHelpFormatting.DocRemarksLine(\"{Escape(validationLine)}\")}}\");");
		}
		else if (string.IsNullOrEmpty(desc))
		{
			sb.AppendLine(
				$"{lineIndent}Console.Error.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {{CliHelpFormatting.DocRemarksLine(\"{Escape(validationLine)}\")}}\");");
		}
		else
		{
			sb.AppendLine(
				$"{lineIndent}Console.Error.WriteLine($\"  {{CliHelpFormatting.Accent(\"{Escape(left)}\")}}  {EscapeForHelpInterpolation(desc)} {{CliHelpFormatting.DocRemarksLine(\"{Escape(validationLine)}\")}}\");");
		}
	}

	private static void EmitOptionsTryParseFlagHelpPrinter(
		StringBuilder sb,
		string parseMethodName,
		List<ParameterModel> flagMembers,
		int maxOptWidth)
	{
		if (flagMembers.Count == 0)
			return;

		var methodName = parseMethodName + "_FlagHelp_ToStdErr";
		sb.AppendLine($"\t\tprivate static void {methodName}(string canonFlagName)");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tswitch (canonFlagName)");
		sb.AppendLine("\t\t\t{");
		foreach (var p in flagMembers.OrderBy(static x => x.CliLongName, StringComparer.OrdinalIgnoreCase))
		{
			sb.AppendLine($"\t\t\t\tcase \"{Escape(p.CliLongName)}\":");
			EmitHelpOptionRowsStdErr(sb, p, maxOptWidth, "\t\t\t\t\t");
			sb.AppendLine("\t\t\t\t\tbreak;");
		}

		sb.AppendLine("\t\t\t\tdefault:");
		sb.AppendLine("\t\t\t\t\tbreak;");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
	}

	/// <summary>After a CLI parse/validation error on stderr: optional flag rows (matching --help), then optional run hint.</summary>
	private static void EmitAfterCliParseErrorHelp(
		StringBuilder sb,
		ParameterModel p,
		string lineIndent,
		string? helpMethodName,
		string? flagHelpStdErrMethodName,
		string? parseFailureRunHint)
	{
		if (p.Kind == ParameterKind.Flag && flagHelpStdErrMethodName is not null)
		{
			sb.AppendLine($"{lineIndent}Console.Error.WriteLine();");
			sb.AppendLine($"{lineIndent}{flagHelpStdErrMethodName}(\"{Escape(p.CliLongName)}\");");
			sb.AppendLine($"{lineIndent}Console.Error.WriteLine();");
			if (parseFailureRunHint is not null)
				sb.AppendLine($"{lineIndent}Console.Error.WriteLine(\"{parseFailureRunHint}\");");
		}
		else if (helpMethodName is not null)
			sb.AppendLine($"{lineIndent}{helpMethodName}();");
	}

	/// <summary>After a validation-check error: optional flag rows on stderr, then optional run hint.</summary>
	private static void EmitValidationErrorFooter(
		StringBuilder sb,
		ParameterModel p,
		string cliName,
		string indent,
		string? flagHelpStdErrMethodName,
		string? runHint)
	{
		if (p.Kind == ParameterKind.Flag && flagHelpStdErrMethodName is not null)
		{
			sb.AppendLine($"{indent}Console.Error.WriteLine();");
			sb.AppendLine($"{indent}{flagHelpStdErrMethodName}(\"{Escape(cliName)}\");");
			sb.AppendLine($"{indent}Console.Error.WriteLine();");
		}

		if (runHint is not null)
			sb.AppendLine($"{indent}Console.Error.WriteLine(\"{runHint}\");");
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

		var widthCandidates = new List<int> { "-h, --help".Length };
		widthCandidates.AddRange(globalFlagMembers.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		foreach ((_, var rows) in namespaceOptionSections)
			widthCandidates.AddRange(rows.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));

		widthCandidates.AddRange(commandOnlyFlags.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		var maxOptWidth = Math.Min(widthCandidates.Max(), 40);
		maxOptWidth = Math.Max(maxOptWidth, "-h, --help".Length);

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
				.Select(p =>
				{
					if (p.IsVariadic)
						return (p.IsRequired ? $"<{p.CliLongName}...>" : $"[<{p.CliLongName}...>]").Length;
					return (p.IsRequired ? $"<{p.CliLongName}>" : $"[<{p.CliLongName}>]").Length;
				})
				.DefaultIfEmpty(0).Max();
			maxArgWidth = Math.Min(maxArgWidth, 40);

			sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Arguments:\"));");
			foreach (var p in cmd.Parameters)
			{
				if (p.Kind != ParameterKind.Positional)
					continue;

				var nameCell = p.IsVariadic
					? (p.IsRequired ? $"<{p.CliLongName}...>" : $"[<{p.CliLongName}...>]")
					: (p.IsRequired ? $"<{p.CliLongName}>" : $"[<{p.CliLongName}>]");
				var nameCellPadded = nameCell.PadRight(maxArgWidth);
				var desc = BuildDescriptionSuffix(p, forPositional: true);
				var argValidationLine = BuildValidationLine(p);
				if (argValidationLine is null)
				{
					sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Placeholder(\"{Escape(nameCellPadded)}\")}}  {EscapeForHelpInterpolation(desc)}\");");
				}
				else if (string.IsNullOrEmpty(desc))
				{
					sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Placeholder(\"{Escape(nameCellPadded)}\")}}  {{CliHelpFormatting.DocRemarksLine(\"{Escape(argValidationLine)}\")}}\");");
				}
				else
				{
					sb.AppendLine($"\t\t\tConsole.Out.WriteLine($\"  {{CliHelpFormatting.Placeholder(\"{Escape(nameCellPadded)}\")}}  {EscapeForHelpInterpolation(desc)} {{CliHelpFormatting.DocRemarksLine(\"{Escape(argValidationLine)}\")}}\");");
				}
			}

			sb.AppendLine("\t\t\tConsole.Out.WriteLine();");
		}

		sb.AppendLine("\t\t\tConsole.Out.WriteLine(CliHelpFormatting.Section(\"Global options:\"));");
		sb.AppendLine(
			$"\t\t\tConsole.Out.WriteLine(\"  \" + CliHelpFormatting.Placeholder(\"{Escape("-h, --help".PadRight(maxOptWidth))}\") + \"  Show help.\");");
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

	private static void EmitCommandFlagHelpToStdErrMethod(StringBuilder sb, CommandModel cmd, AppEmitModel app)
	{
		if (cmd.IsRootDefault)
			return;

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

		var widthCandidates = new List<int> { "-h, --help".Length };
		widthCandidates.AddRange(globalFlagMembers.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		foreach ((_, var rows) in namespaceOptionSections)
			widthCandidates.AddRange(rows.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));

		widthCandidates.AddRange(commandOnlyFlags.Select(p => HelpLayout.FormatOptionLeftCell(p).Length));
		var maxOptWidth = Math.Min(widthCandidates.Max(), 40);
		maxOptWidth = Math.Max(maxOptWidth, "-h, --help".Length);

		var byCanon = new Dictionary<string, ParameterModel>(StringComparer.OrdinalIgnoreCase);
		foreach (var p in globalFlagMembers)
			byCanon[p.CliLongName] = p;
		foreach ((_, var rows) in namespaceOptionSections)
			foreach (var p in rows)
				byCanon[p.CliLongName] = p;
		foreach (var p in commandOnlyFlags)
			byCanon[p.CliLongName] = p;

		sb.AppendLine($"\t\tprivate static void PrintHelp_{cmd.RunMethodName}_Flag_ToStdErr(string canonFlagName)");
		sb.AppendLine("\t\t{");
		if (byCanon.Count > 0)
		{
			sb.AppendLine("\t\t\tswitch (canonFlagName)");
			sb.AppendLine("\t\t\t{");
			foreach (var p in byCanon.Values.OrderBy(static x => x.CliLongName, StringComparer.OrdinalIgnoreCase))
			{
				sb.AppendLine($"\t\t\t\tcase \"{Escape(p.CliLongName)}\":");
				EmitHelpOptionRowsStdErr(sb, p, maxOptWidth, "\t\t\t\t\t");
				sb.AppendLine("\t\t\t\t\tbreak;");
			}

			sb.AppendLine("\t\t\t\tdefault:");
			sb.AppendLine("\t\t\t\t\tbreak;");
			sb.AppendLine("\t\t\t}");
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

		if (forPositional && p.IsVariadic)
			parts.Add("[variadic]");

		if (!string.IsNullOrWhiteSpace(p.Description))
			parts.Add(p.Description.Trim());

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

		if (p.ScalarKind == CliScalarKind.Enum && !p.EnumMemberNames.IsDefaultOrEmpty)
		{
			var lit = p.DefaultValueLiteral.Trim();
			for (var i = 0; i < p.EnumMemberNames.Length; i++)
			{
				var member = p.EnumMemberNames[i];
				if (string.Equals(lit, member, StringComparison.Ordinal) || lit.EndsWith("." + member, StringComparison.Ordinal))
					return ResolveEnumMemberCliName(p.EnumMemberCliNames, i, member);
			}
		}

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
					string seg;
					if (p.IsVariadic)
						seg = p.IsRequired ? $"<{p.CliLongName}...>" : $"[<{p.CliLongName}...>]";
					else
						seg = p.IsRequired ? $"<{p.CliLongName}>" : $"[<{p.CliLongName}>]";
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
