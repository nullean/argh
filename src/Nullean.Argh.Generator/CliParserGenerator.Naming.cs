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
	private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

	/// <summary>Doubles <c>{</c> and <c>}</c> so text can be embedded in generated C# <c>$"…"</c> without forming interpolation holes.</summary>
	private static string EscapeInterpolationBraces(string s) =>
		s.Replace("{", "{{").Replace("}", "}}");

	private static string EscapeForHelpInterpolation(string s) => EscapeInterpolationBraces(Escape(s));

	private static string EscapeDocXml(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

	/// <summary>
	/// Remarks XML only: <c>&lt;paramref name="x"/&gt;</c> for a CLI flag becomes <c>&lt;c&gt;--long-name&lt;/c&gt;</c>;
	/// <c>&lt;see cref="M:…"/&gt;</c> for another command handler becomes <c>&lt;c&gt;entryAsm route cmd usage-hints&lt;/c&gt;</c> (same tail as the emitted Usage line after the assembly name).
	/// </summary>
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

	private readonly record struct ParamDoc(char? ShortOpt, ImmutableArray<string> Aliases, string Description, string? ExplicitLongName = null);

	private static class ParamDocParser
	{
		public static ParamDoc Parse(string text)
		{
			text = text.Trim();
			if (text.Length == 0)
				return new ParamDoc(null, ImmutableArray<string>.Empty, "");

			var parts = text.Split(',');
			char? shortOpt = null;
			string? explicitLongName = null;
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
					// First --long-name becomes the primary CLI name (overrides the derived name).
					// Subsequent --long-names become aliases.
					if (explicitLongName is null)
						explicitLongName = seg.Substring(2);
					else
						aliases.Add(seg.Substring(2));
					continue;
				}

				break;
			}

			var desc = i >= parts.Length ? "" : string.Join(",", parts, i, parts.Length - i).Trim();
			return new ParamDoc(shortOpt, aliases.ToImmutable(), desc, explicitLongName);
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
			{
				if (p.ShortOpt is char c)
					return "-" + c + ", " + "--" + p.CliLongName;
				return "--" + p.CliLongName;
			}

			if (p.Special == BoolSpecialKind.NullableBool)
			{
				if (p.ShortOpt is char nc)
					return "-" + nc + ", " + "--[no-]" + p.CliLongName;
				return "--[no-]" + p.CliLongName;
			}

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
			if (InferValidationDerivedTypeHint(p) is string vh)
				return vh;

			switch (p.ScalarKind)
			{
				case CliScalarKind.Collection:
					return "<values>";
				case CliScalarKind.Enum:
				return "<enum>";
				case CliScalarKind.FileInfo:
					return "<file>";
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
				"DateTime" or "DateTime?" => "<dateTime>",
				"DateTimeOffset" or "DateTimeOffset?" => "<dateTimeOffset>",
				"TimeSpan" or "TimeSpan?" => "<timeSpan>",
				"DateOnly" or "DateOnly?" => "<dateOnly>",
				_ => "<value>"
			};
		}

		private static string? InferValidationDerivedTypeHint(ParameterModel p)
		{
			if (p.Validations.IsDefaultOrEmpty || p.ScalarKind == CliScalarKind.Collection)
				return null;

			// Nullable reference/value does not switch placeholders: email is only for CLR string bindings; url for string/Uri scheme rules.
			if (p.Validations.Any(static v => v is EmailConstraint))
			{
				if (HasClrSemanticStringBinding(p))
					return "<email>";
			}

			if (p.Validations.Any(static v => v is UrlConstraint or UriSchemeConstraint))
				return "<url>";

			return null;
		}

		private static bool HasClrSemanticStringBinding(ParameterModel p) =>
			p.ScalarKind == CliScalarKind.Primitive && IsClrStringParameterTypeName(p.TypeName);

		private static bool IsClrStringParameterTypeName(string? typeName) =>
			typeName is not null && (typeName == "string" || typeName == "string?");
	}

	private readonly record struct MethodDocumentation(
		string SummaryOneLiner,
		string RemarksRendered,
		string ExamplesRendered,
		string SummaryInnerXml,
		string RemarksInnerXml,
		ImmutableDictionary<string, string> ParamDocsRaw,
		ImmutableDictionary<string, string> ParamSeparators);

}
