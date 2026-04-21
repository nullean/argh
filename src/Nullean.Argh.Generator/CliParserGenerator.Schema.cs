using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Nullean.Argh;

public sealed partial class CliParserGenerator
{
	private static void EmitRootSchemaBlock(StringBuilder sb, string indent)
	{
		sb.AppendLine(indent + "if (CompletionProtocol.IsSchemaInvocation(args))");
		sb.AppendLine(indent + "{");
		sb.AppendLine(indent + "\tConsole.Out.Write(ArghRuntime.FormatCliSchemaJson());");
		sb.AppendLine(indent + "\treturn 0;");
		sb.AppendLine(indent + "}");
		sb.AppendLine();
	}


	private static void EmitBuildCliSchemaDocumentHierarchical(StringBuilder sb, AppEmitModel app,
		string entryAssemblyName, string entryAssemblyVersion)
	{
		sb.AppendLine("\t\tinternal static ArghCliSchemaDocument BuildCliSchemaDocument() =>");
		sb.AppendLine("\t\t\tnew ArghCliSchemaDocument(");
		sb.AppendLine("\t\t\t\t1,");
		sb.AppendLine($"\t\t\t\t\"{Escape(entryAssemblyName)}\",");
		sb.AppendLine($"\t\t\t\t\"{Escape(entryAssemblyVersion)}\",");
		EmitNullableStringArg(sb, "\t\t\t\t", string.IsNullOrWhiteSpace(app.RootSummary) ? null : app.RootSummary);
		sb.AppendLine(",");
		sb.AppendLine("\t\t\t\tnew[] { \"__complete\", \"__completion\", \"__schema\" },");
		EmitSchemaGlobalOptionsExpression(sb, app, "\t\t\t\t");
		sb.AppendLine(",");
		EmitSchemaRootDefaultExpression(sb, app.Root.RootCommand, entryAssemblyName, "\t\t\t\t");
		sb.AppendLine(",");
		EmitSchemaRootCommandsExpression(sb, app, entryAssemblyName, "\t\t\t\t");
		sb.AppendLine(",");
		EmitSchemaNamespacesExpression(sb, app.Root.Children, entryAssemblyName, "\t\t\t\t");
		sb.AppendLine(");");
	}

	private static void EmitSchemaGlobalOptionsExpression(StringBuilder sb, AppEmitModel app, string indent)
	{
		if (app.GlobalOptionsModel is not { } gom || gom.Members.IsEmpty)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<CliParameterSchema>()");
			return;
		}

		var flags = gom.Members.Where(static p => p.Kind == ParameterKind.Flag).ToList();
		if (flags.Count == 0)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<CliParameterSchema>()");
			return;
		}

		sb.AppendLine(indent + "new CliParameterSchema[]");
		sb.AppendLine(indent + "{");
		foreach (var p in flags)
			sb.AppendLine($"{indent}\t{EmitCliParameterSchemaNewExpression(p)},");
		sb.Append(indent);
		sb.Append("}");
	}

	private static void EmitSchemaRootDefaultExpression(StringBuilder sb, CommandModel? rootCmd, string entryAssemblyName,
		string indent)
	{
		if (rootCmd is not { IsRootDefault: true } rc)
		{
			sb.Append(indent);
			sb.Append("null");
			return;
		}

		sb.AppendLine(indent + "new CliDefaultHandlerSchema(");
		sb.AppendLine($"{indent}\t\"root\",");
		EmitNullableStringArg(sb, $"{indent}\t", rc.SummaryOneLiner);
		sb.AppendLine(",");
		EmitNullableStringArg(sb, $"{indent}\t", rc.RemarksRendered);
		sb.AppendLine(",");
		EmitNullableStringArg(sb, $"{indent}\t", BuildCommandUsageSynopsisTail(rc, entryAssemblyName));
		sb.AppendLine(",");
		EmitExamplesStringArray(sb, rc.ExamplesRendered, $"{indent}\t");
		sb.AppendLine(",");
		EmitSchemaParametersForCommand(sb, rc.Parameters, $"{indent}\t");
		sb.AppendLine();
		sb.Append(indent);
		sb.Append(")");
	}

	private static void EmitSchemaDefaultHandlerForNamespace(StringBuilder sb, CommandModel? rootCmd,
		string entryAssemblyName, string indent)
	{
		if (rootCmd is not { IsRootDefault: true } rc)
		{
			sb.Append(indent);
			sb.Append("null");
			return;
		}

		sb.AppendLine(indent + "new CliDefaultHandlerSchema(");
		sb.AppendLine($"{indent}\t\"namespace\",");
		EmitNullableStringArg(sb, $"{indent}\t", rc.SummaryOneLiner);
		sb.AppendLine(",");
		EmitNullableStringArg(sb, $"{indent}\t", rc.RemarksRendered);
		sb.AppendLine(",");
		EmitNullableStringArg(sb, $"{indent}\t", BuildCommandUsageSynopsisTail(rc, entryAssemblyName));
		sb.AppendLine(",");
		EmitExamplesStringArray(sb, rc.ExamplesRendered, $"{indent}\t");
		sb.AppendLine(",");
		EmitSchemaParametersForCommand(sb, rc.Parameters, $"{indent}\t");
		sb.AppendLine();
		sb.Append(indent);
		sb.Append(")");
	}


	private static void EmitSchemaRootCommandsExpression(StringBuilder sb, AppEmitModel app, string entryAssemblyName,
		string indent)
	{
		var cmds = app.Root.Commands.Where(static c => !c.IsRootDefault).OrderBy(c => c.CommandName, StringComparer.Ordinal)
			.ToList();
		if (cmds.Count == 0)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<CliCommandSchema>()");
			return;
		}

		sb.AppendLine(indent + "new CliCommandSchema[]");
		sb.AppendLine(indent + "{");
		foreach (var cmd in cmds)
		{
			EmitCliCommandSchemaBody(sb, cmd, entryAssemblyName, indent + "\t");
			sb.AppendLine(",");
		}

		sb.Append(indent);
		sb.Append("}");
	}

	private static void EmitCliCommandSchemaBody(StringBuilder sb, CommandModel cmd, string entryAssemblyName, string indent)
	{
		sb.AppendLine(indent + "new CliCommandSchema(");
		EmitImmutableStringArrayInline(sb, cmd.RoutePrefix, $"{indent}\t");
		sb.AppendLine(",");
		sb.AppendLine($"{indent}\t\"{Escape(cmd.CommandName)}\",");
		EmitNullableStringArg(sb, $"{indent}\t", cmd.SummaryOneLiner);
		sb.AppendLine(",");
		EmitNullableStringArg(sb, $"{indent}\t", cmd.RemarksRendered);
		sb.AppendLine(",");
		EmitNullableStringArg(sb, $"{indent}\t", BuildCommandUsageSynopsisTail(cmd, entryAssemblyName));
		sb.AppendLine(",");
		EmitExamplesStringArray(sb, cmd.ExamplesRendered, $"{indent}\t");
		sb.AppendLine(",");
		EmitSchemaParametersForCommand(sb, cmd.Parameters, $"{indent}\t");
		sb.Append(indent);
		sb.Append(")");
	}

	private static void EmitImmutableStringArrayInline(StringBuilder sb, ImmutableArray<string> prefix, string indent)
	{
		if (prefix.IsDefaultOrEmpty)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<string>()");
			return;
		}

		sb.AppendLine(indent + "new string[]");
		sb.AppendLine(indent + "{");
		foreach (var s in prefix)
			sb.AppendLine($"{indent}\t\"{Escape(s)}\",");
		sb.Append(indent);
		sb.Append("}");
	}

	private static void EmitSchemaNamespacesExpression(StringBuilder sb,
		List<RegistryNode.NamedCommandNamespaceChild> children, string entryAssemblyName, string indent)
	{
		if (children.Count == 0)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<CliNamespaceSchema>()");
			return;
		}

		sb.AppendLine(indent + "new CliNamespaceSchema[]");
		sb.AppendLine(indent + "{");
		foreach (var ch in children.OrderBy(c => c.Segment, StringComparer.OrdinalIgnoreCase))
		{
			EmitCliNamespaceSchemaBody(sb, ch, entryAssemblyName, indent + "\t");
			sb.AppendLine(",");
		}

		sb.Append(indent);
		sb.Append("}");
	}

	private static void EmitCliNamespaceSchemaBody(StringBuilder sb, RegistryNode.NamedCommandNamespaceChild ch,
		string entryAssemblyName, string indent)
	{
		var node = ch.Node;
		var notes = FlattenTypeRemarksInnerXml(node.RemarksInnerXml);
		sb.AppendLine(indent + "new CliNamespaceSchema(");
		sb.AppendLine($"{indent}\t\"{Escape(ch.Segment)}\",");
		EmitNullableStringArg(sb, $"{indent}\t", ch.SummaryOneLiner);
		sb.AppendLine(",");
		EmitNullableStringArg(sb, $"{indent}\t", notes);
		sb.AppendLine(",");
		EmitSchemaNamespaceOptionsExpression(sb, node, $"{indent}\t");
		sb.AppendLine(",");
		EmitSchemaDefaultHandlerForNamespace(sb, node.RootCommand, entryAssemblyName, $"{indent}\t");
		sb.AppendLine(",");
		EmitSchemaNamespaceCommandsExpression(sb, node, entryAssemblyName, $"{indent}\t");
		sb.AppendLine(",");
		EmitSchemaNamespacesExpression(sb, node.Children, entryAssemblyName, $"{indent}\t");
		sb.AppendLine();
		sb.Append(indent);
		sb.Append(")");
	}

	private static string FlattenTypeRemarksInnerXml(string? innerXml)
	{
		if (string.IsNullOrWhiteSpace(innerXml))
			return "";
		var wrapped = "<remarks>" + innerXml + "</remarks>";
		return Documentation.ParseMethod(wrapped, CSharpParseOptions.Default).RemarksRendered;
	}

	private static void EmitSchemaNamespaceOptionsExpression(StringBuilder sb, RegistryNode node, string indent)
	{
		if (node.CommandNamespaceOptionsModel is not { Members.Length: > 0 } nom)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<CliParameterSchema>()");
			return;
		}

		var flags = nom.Members.Where(static p => p.Kind == ParameterKind.Flag).ToList();
		if (flags.Count == 0)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<CliParameterSchema>()");
			return;
		}

		sb.AppendLine(indent + "new CliParameterSchema[]");
		sb.AppendLine(indent + "{");
		foreach (var p in flags)
			sb.AppendLine($"{indent}\t{EmitCliParameterSchemaNewExpression(p)},");
		sb.Append(indent);
		sb.Append("}");
	}

	private static void EmitSchemaNamespaceCommandsExpression(StringBuilder sb, RegistryNode node, string entryAssemblyName,
		string indent)
	{
		var cmds = node.Commands.Where(static c => !c.IsRootDefault).OrderBy(c => c.CommandName, StringComparer.Ordinal)
			.ToList();
		if (cmds.Count == 0)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<CliCommandSchema>()");
			return;
		}

		sb.AppendLine(indent + "new CliCommandSchema[]");
		sb.AppendLine(indent + "{");
		foreach (var cmd in cmds)
		{
			EmitCliCommandSchemaBody(sb, cmd, entryAssemblyName, indent + "\t");
			sb.AppendLine(",");
		}

		sb.Append(indent);
		sb.Append("}");
	}

	private static void EmitSchemaParametersForCommand(StringBuilder sb, ImmutableArray<ParameterModel> parameters,
		string indent)
	{
		var list = parameters.Where(static p => p.Kind != ParameterKind.Injected).ToList();
		if (list.Count == 0)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<CliParameterSchema>()");
			return;
		}

		sb.AppendLine(indent + "new CliParameterSchema[]");
		sb.AppendLine(indent + "{");
		foreach (var p in list)
			sb.AppendLine($"{indent}\t{EmitCliParameterSchemaNewExpression(p)},");
		sb.Append(indent);
		sb.Append("}");
	}

	private static void EmitNullableStringArg(StringBuilder sb, string indent, string? value)
	{
		sb.Append(indent);
		if (string.IsNullOrWhiteSpace(value))
			sb.Append("null");
		else
			sb.Append('"').Append(Escape(value!)).Append('"');
	}

	private static void EmitExamplesStringArray(StringBuilder sb, string? examplesRendered, string indent)
	{
		if (string.IsNullOrWhiteSpace(examplesRendered))
		{
			sb.Append(indent);
			sb.Append("Array.Empty<string>()");
			return;
		}

		var raw = examplesRendered!;
		var parts = raw.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.None)
			.Select(s => s.Trim())
			.Where(s => s.Length > 0)
			.ToList();
		if (parts.Count == 0)
		{
			sb.Append(indent);
			sb.Append("Array.Empty<string>()");
			return;
		}

		sb.AppendLine(indent + "new string[]");
		sb.AppendLine(indent + "{");
		foreach (var p in parts)
			sb.AppendLine($"{indent}\t\"{Escape(p)}\",");
		sb.Append(indent);
		sb.Append("}");
	}

	private static string EmitCliParameterSchemaNewExpression(ParameterModel p)
	{
		var role = p.Kind == ParameterKind.Positional ? "positional" : "flag";
		var shortName = p.ShortOpt is char c ? $"\"{Escape(c.ToString())}\"" : "null";
		var kind = Escape(SchemaParameterKind(p));
		var req = p.IsRequired ? "true" : "false";
		var summary = string.IsNullOrEmpty(p.Description) ? "null" : $"\"{Escape(p.Description)}\"";
		var validations = BuildConstraintsExpression(p.Validations);
		return
			$"new CliParameterSchema(\"{role}\", \"{Escape(p.CliLongName)}\", {shortName}, \"{kind}\", {req}, {summary}, {validations})";
	}

	private static string BuildConstraintsExpression(ImmutableArray<ValidationConstraint> validations)
	{
		if (validations.IsDefaultOrEmpty)
			return "null";

		var parts = new System.Collections.Generic.List<string>();
		foreach (var v in validations)
		{
			switch (v)
			{
				case RangeConstraint r:
					parts.Add($"new CliConstraintSchema(\"range\", Min: \"{Escape(r.MinLiteral)}\", Max: \"{Escape(r.MaxLiteral)}\")");
					break;
				case StringLengthConstraint sl:
					var slMin = sl.Min.HasValue ? $"\"{sl.Min.Value}\"" : "null";
					var slMax = sl.Max.HasValue ? $"\"{sl.Max.Value}\"" : "null";
					parts.Add($"new CliConstraintSchema(\"length\", Min: {slMin}, Max: {slMax})");
					break;
				case RegexConstraint re:
					parts.Add($"new CliConstraintSchema(\"regex\", Pattern: \"{Escape(re.Pattern)}\")");
					break;
				case AllowedValuesConstraint av:
					parts.Add($"new CliConstraintSchema(\"allowed\", Values: new string[] {{ {string.Join(", ", av.Values.Select(val => $"\"{Escape(val)}\""))} }})");
					break;
				case DeniedValuesConstraint dv:
					parts.Add($"new CliConstraintSchema(\"denied\", Values: new string[] {{ {string.Join(", ", dv.Values.Select(val => $"\"{Escape(val)}\""))} }})");
					break;
				case EmailConstraint:
					parts.Add("new CliConstraintSchema(\"email\")");
					break;
				case UrlConstraint:
					parts.Add("new CliConstraintSchema(\"url\")");
					break;
			}
		}

		if (parts.Count == 0)
			return "null";

		return $"new CliConstraintSchema[] {{ {string.Join(", ", parts)} }}";
	}

	private static string SchemaParameterKind(ParameterModel p)
	{
		if (p.ScalarKind == CliScalarKind.Collection)
			return $"Collection<{p.ElementTypeName}>";
		if (p.ScalarKind == CliScalarKind.Enum && !string.IsNullOrEmpty(p.EnumTypeFq))
		{
			var name = p.EnumTypeFq!.Split('.').Last();
			return $"Enum:{name}";
		}

		return $"{p.ScalarKind}:{p.TypeName}";
	}

}
