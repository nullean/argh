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
		sb.AppendLine("\t\t\t\t2,");
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
		if (!cmd.CommandAliases.IsDefaultOrEmpty)
		{
			sb.AppendLine(",");
			var aliasArr = string.Join(", ", cmd.CommandAliases.Select(a => $"\"{Escape(a)}\""));
			sb.Append($"{indent}\tAliases: new string[] {{ {aliasArr} }}");
		}
		if (cmd.IsHidden)
		{
			sb.AppendLine(",");
			sb.Append($"{indent}\tHidden: true");
		}
		sb.AppendLine();
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
		var type = MapToJsonSchemaType(p.ScalarKind, p.TypeName);
		var req = p.IsRequired ? "true" : "false";
		var summary = string.IsNullOrEmpty(p.Description) ? "null" : $"\"{Escape(p.Description)}\"";

		var sb = new StringBuilder();
		sb.Append($"new CliParameterSchema(\"{role}\", \"{Escape(p.CliLongName)}\", {shortName}, \"{type}\", {req}, {summary}");

		var defaultVal = GetSchemaDefaultValue(p);
		if (defaultVal is not null)
			sb.Append($", DefaultValue: \"{Escape(defaultVal)}\"");

		if (p.IsCollection)
		{
			if (p.CollectionSeparator is null)
				sb.Append(", Repeatable: true");
			else
				sb.Append($", Separator: \"{Escape(p.CollectionSeparator)}\"");
		}

		if (!p.Aliases.IsDefaultOrEmpty)
		{
			var aliasArr = string.Join(", ", p.Aliases.Select(a => $"\"{Escape(a)}\""));
			sb.Append($", Aliases: new string[] {{ {aliasArr} }}");
		}

		if (type == "enum" && !p.EnumMemberNames.IsDefaultOrEmpty)
		{
			var enumArr = string.Join(", ", p.EnumMemberNames.Select(m => $"\"{Escape(m.ToLowerInvariant())}\""));
			sb.Append($", EnumValues: new string[] {{ {enumArr} }}");
		}

		if (p.IsCollection)
		{
			var elemType = MapToJsonSchemaType(p.ElementScalarKind, p.ElementTypeName);
			sb.Append($", ElementType: \"{elemType}\"");
		}

		if (p.IsHidden)
			sb.Append(", Hidden: true");

		var validations = BuildConstraintsExpression(p.Validations, p.ExpandUserProfileBeforeBind);
		if (validations != "null")
			sb.Append($", Validations: {validations}");

		sb.Append(")");
		return sb.ToString();
	}

	private static string? GetSchemaDefaultValue(ParameterModel p)
	{
		if (p.DefaultValueLiteral is null)
			return null;
		var formatted = FormatDefaultForHelp(p);
		if (string.IsNullOrEmpty(formatted) || formatted == "null")
			return null;
		return formatted;
	}

	private static string MapToJsonSchemaType(CliScalarKind sk, string typeName)
	{
		if (sk == CliScalarKind.Enum)
			return "enum";
		if (sk == CliScalarKind.Collection)
			return "array";
		if (sk != CliScalarKind.Primitive)
			return "string";

		// GetSimpleTypeName returns nullable variants with a trailing '?' (e.g. "int?", "bool?")
		var t = typeName.TrimEnd('?');
		return t switch
		{
			"String" or "string" or "Char" or "char" => "string",
			"Boolean" or "bool" => "boolean",
			"Int16" or "Int32" or "Int64" or "UInt16" or "UInt32" or "UInt64"
				or "Byte" or "SByte" or "short" or "int" or "long"
				or "ushort" or "uint" or "ulong" or "byte" or "sbyte" => "integer",
			"Single" or "Double" or "Decimal" or "float" or "double" or "decimal" => "number",
			_ => "string"
		};
	}

	private static string BuildConstraintsExpression(ImmutableArray<ValidationConstraint> validations, bool expandUserProfile)
	{
		var parts = new System.Collections.Generic.List<string>();

		if (!validations.IsDefaultOrEmpty)
		{
			foreach (var v in validations)
			{
				switch (v)
				{
					case RangeConstraint r:
						parts.Add($"new CliConstraintSchema(\"range\", Min: \"{Escape(r.MinLiteral)}\", Max: \"{Escape(r.MaxLiteral)}\")");
						break;
					case TimeSpanRangeConstraint ts:
						parts.Add($"new CliConstraintSchema(\"timeSpanRange\", Min: \"{Escape(ts.MinLiteral)}\", Max: \"{Escape(ts.MaxLiteral)}\")");
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
					case UriSchemeConstraint us:
						parts.Add($"new CliConstraintSchema(\"uriScheme\", Values: new string[] {{ {string.Join(", ", us.Schemes.Select(s => $"\"{Escape(s)}\""))} }})");
						break;
					case FileExtensionsConstraint fe:
						parts.Add($"new CliConstraintSchema(\"fileExtensions\", Values: new string[] {{ {string.Join(", ", fe.Extensions.Select(ext => $"\"{Escape(ext)}\""))} }})");
						break;
					case ExistingPathConstraint:
						parts.Add("new CliConstraintSchema(\"existing\")");
						break;
					case NonExistingPathConstraint:
						parts.Add("new CliConstraintSchema(\"nonExisting\")");
						break;

case RejectSymbolicLinksConstraint:
						parts.Add("new CliConstraintSchema(\"rejectSymbolicLinks\")");
						break;
				}
			}
		}

		if (expandUserProfile)
			parts.Add("new CliConstraintSchema(\"expandUserProfile\")");

		if (parts.Count == 0)
			return "null";

		return $"new CliConstraintSchema[] {{ {string.Join(", ", parts)} }}";
	}

}
