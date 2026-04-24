using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nullean.Argh.Help;

/// <summary>Renders XML doc comment inner XML as formatted, optionally ANSI-colored terminal text.</summary>
public static class XmlDocumentationRenderer
{
	/// <summary>
	/// Writes the content of an XML doc element (e.g. a <c>&lt;summary&gt;</c> or <c>&lt;remarks&gt;</c> inner XML)
	/// to <paramref name="writer"/>, applying terminal formatting and indenting each output line with <paramref name="lineIndent"/>.
	/// </summary>
	/// <param name="writer">Target text writer.</param>
	/// <param name="lineIndent">Prefix prepended to every output line.</param>
	/// <param name="elementInnerXml">Raw inner XML of the doc element.</param>
	/// <param name="isRemarks"><see langword="true"/> when rendering a <c>&lt;remarks&gt;</c> block (preserves line breaks); <see langword="false"/> for inline flow (collapses whitespace).</param>
	public static void WriteIndentedDoc(TextWriter writer, string lineIndent, string elementInnerXml, bool isRemarks)
	{
		if (string.IsNullOrWhiteSpace(elementInnerXml))
			return;

		try
		{
			var wrapper = XElement.Parse("<x>" + elementInnerXml + "</x>", LoadOptions.PreserveWhitespace);
			var rendered = RenderNodes(wrapper.Nodes(), isRemarks).Replace("\r\n", "\n");
			if (!isRemarks)
			{
				// Single flow: comment line breaks become insignificant whitespace between tags.
				rendered = Regex.Replace(rendered, @"\s+", " ").Trim();
			}
			else
			{
				// Remarks: avoid stacked blank lines from para/list (para behaves like <br/>, not blank paragraphs).
				rendered = CollapseBlankRuns(rendered).TrimEnd('\n', '\r');
			}

			WriteRenderedLines(writer, lineIndent, rendered, isRemarks);
		}
		catch
		{
			var fallback = elementInnerXml.Replace("\r\n", "\n");
			if (!isRemarks)
				fallback = Regex.Replace(fallback, @"\s+", " ").Trim();
			else
				fallback = CollapseBlankRuns(fallback).TrimEnd('\n', '\r');
			WriteRenderedLines(writer, lineIndent, fallback, isRemarks);
		}
	}

	private static void WriteRenderedLines(TextWriter writer, string lineIndent, string rendered, bool isRemarks)
	{
		foreach (var part in rendered.Split('\n'))
		{
			var line = part.TrimEnd('\r');
			if (string.IsNullOrEmpty(line))
			{
				writer.WriteLine();
				continue;
			}

			string output;
			if (line.IndexOf('\x1b') >= 0)
				output = line;
			else
				output = isRemarks ? CliHelpFormatting.DocRemarksLine(line) : CliHelpFormatting.DocSummaryLine(line);
			if (CliHelpFormatting.UseAnsiColors && output.IndexOf('\x1b') >= 0)
				output += "\x1b[0m";
			writer.WriteLine(lineIndent + output);
		}
	}

	/// <summary>Merge runs of 2+ newlines into a single newline so remarks are not padded with empty rows.</summary>
	private static string CollapseBlankRuns(string rendered)
	{
		if (string.IsNullOrEmpty(rendered))
			return rendered;
		return Regex.Replace(rendered, @"\n{2,}", "\n");
	}

	private static string RenderNodes(IEnumerable<XNode> nodes, bool inRemarks)
	{
		var sb = new StringBuilder();
		FlattenNodes(nodes, sb, inRemarks);
		return sb.ToString();
	}

	private static void FlattenNodes(IEnumerable<XNode> nodes, StringBuilder sb, bool inRemarks)
	{
		foreach (var n in nodes)
		{
			switch (n)
			{
				case XText t:
					if (string.IsNullOrEmpty(t.Value))
						break;
					sb.Append(inRemarks ? CliHelpFormatting.DocRemarksPlainText(t.Value) : CliHelpFormatting.DocSummaryPlainText(t.Value));
					break;
				case XElement e when e.Name.LocalName == "para":
					if (sb.Length > 0)
						sb.AppendLine();
					FlattenNodes(e.Nodes(), sb, inRemarks);
					break;
				case XElement e when e.Name.LocalName == "br":
					sb.AppendLine();
					break;
				case XElement e when e.Name.LocalName == "code":
					foreach (var c in e.Nodes())
					{
						if (c is not XText tx)
							continue;
						var raw = tx.Value.Replace("\r\n", "\n");
						foreach (var seg in raw.Split('\n'))
						{
							var trimmed = seg.TrimEnd();
							if (trimmed.Length == 0)
								continue;
							sb.Append(CliHelpFormatting.CodeBlockLine(CliHelpFormatting.XmlDocCodeLinePrefix + trimmed)).AppendLine();
						}
					}

					break;
				case XElement e when e.Name.LocalName == "c":
					sb.Append(CliHelpFormatting.DocInlineCodeSpan(e.Value.Trim(), inRemarks));
					break;
				case XElement e when e.Name.LocalName == "b":
					sb.Append(CliHelpFormatting.DocBoldSpan(CollectPlain(e), inRemarks));
					break;
				case XElement e when e.Name.LocalName == "i":
					sb.Append(CliHelpFormatting.DocItalicSpan(CollectPlain(e), inRemarks));
					break;
				case XElement e when e.Name.LocalName == "u":
					sb.Append(CliHelpFormatting.DocUnderlineSpan(CollectPlain(e), inRemarks));
					break;
				case XElement e when e.Name.LocalName == "paramref":
				{
					var name = e.Attribute("name")?.Value;
					if (name != null && !string.IsNullOrEmpty(name))
						sb.Append(CliHelpFormatting.DocParamRef(name, inRemarks));
					break;
				}
				case XElement e when e.Name.LocalName == "typeparamref":
				{
					var name = e.Attribute("name")?.Value;
					if (name != null && !string.IsNullOrEmpty(name))
						sb.Append(CliHelpFormatting.DocParamRef(name, inRemarks));
					break;
				}
				case XElement { Name.LocalName: "see" } e:
					AppendSeeElement(e, sb, inRemarks);
					break;
				case XElement { Name.LocalName: "a" } e:
				{
					var href = e.Attribute("href")?.Value;
					var vis = e.Value.Trim();
					if (string.IsNullOrEmpty(vis))
						vis = href ?? "";
					sb.Append(CliHelpFormatting.Osc8Hyperlink(href ?? "", vis, inRemarks));
					break;
				}
				case XElement e when e.Name.LocalName == "exception":
				{
					var cref = e.Attribute("cref")?.Value;
					if (cref != null && !string.IsNullOrEmpty(cref))
					{
						sb.Append(CliHelpFormatting.DocRefCref(CrefShortName(cref), inRemarks));
						sb.Append(' ');
					}
					FlattenNodes(e.Nodes(), sb, inRemarks);
					break;
				}
				case XElement e when e.Name.LocalName == "list":
					if (sb.Length > 0)
						sb.AppendLine();
					var listType = e.Attribute("type")?.Value;
					var num = 1;
					foreach (var item in e.Elements().Where(x => x.Name.LocalName == "item"))
					{
						if (listType == "number")
							sb.Append("  ").Append(num++).Append(". ");
						else
							sb.Append("  - ");
						var desc = item.Element("description");
						if (desc is not null)
							FlattenNodes(desc.Nodes(), sb, inRemarks);
						else
							FlattenNodes(item.Nodes(), sb, inRemarks);
						sb.AppendLine();
					}
					break;
				case XElement e when e.Name.LocalName == "example":
					if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
						sb.AppendLine();
					sb.Append(CliHelpFormatting.ExampleSectionTitle());
					sb.AppendLine();
					FlattenNodes(e.Nodes(), sb, inRemarks);
					break;
				case XElement e:
					FlattenNodes(e.Nodes(), sb, inRemarks);
					break;
			}
		}
	}

	private static string CollectPlain(XElement e)
	{
		var sb = new StringBuilder();
		FlattenPlain(e.Nodes(), sb);
		return sb.ToString();
	}

	private static void FlattenPlain(IEnumerable<XNode> nodes, StringBuilder sb)
	{
		foreach (var n in nodes)
		{
			switch (n)
			{
				case XText t:
					sb.Append(t.Value);
					break;
				case XElement el:
					FlattenPlain(el.Nodes(), sb);
					break;
			}
		}
	}

	private static void AppendSeeElement(XElement e, StringBuilder sb, bool inRemarks)
	{
		var lang = e.Attribute("langword")?.Value;
		if (lang != null && !string.IsNullOrEmpty(lang))
		{
			sb.Append(CliHelpFormatting.DocLangwordSpan(lang, inRemarks));
			return;
		}

		var href = e.Attribute("href")?.Value;
		if (href != null && !string.IsNullOrEmpty(href))
		{
			var vis = string.IsNullOrWhiteSpace(e.Value) ? href : e.Value.Trim();
			sb.Append(CliHelpFormatting.Osc8Hyperlink(href, vis, inRemarks));
			return;
		}

		var cref = e.Attribute("cref")?.Value;
		if (cref != null && !string.IsNullOrEmpty(cref))
		{
			var vis = string.IsNullOrWhiteSpace(e.Value) ? CrefShortName(cref) : e.Value.Trim();
			sb.Append(CliHelpFormatting.DocRefCref(vis, inRemarks));
			return;
		}

		FlattenNodes(e.Nodes(), sb, inRemarks);
	}

	private static string CrefShortName(string cref)
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
