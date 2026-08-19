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
	private static string ExtractDocumentationFromTriviaList(SyntaxTriviaList triviaList)
	{
		// Fast path: structured documentation trivia (DocumentationMode=Parse or Diagnose)
		foreach (var trivia in triviaList)
		{
			if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) &&
			    !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
				continue;
			var stripped = DocTriviaStripPattern.Replace(trivia.ToFullString(), "").Trim();
			if (stripped.Length > 0)
				return stripped;
		}

		// Slow path: plain comment trivia (DocumentationMode=None, i.e. GenerateDocumentationFile not set).
		// Collect consecutive /// lines immediately preceding the token.
		var sb = new StringBuilder();
		foreach (var trivia in triviaList)
		{
			if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
			{
				var s = trivia.ToString();
				if (s.StartsWith("///", StringComparison.Ordinal))
				{
					sb.AppendLine(s);
					continue;
				}
			}
			// Non-doc trivia resets the accumulator so we only keep the block immediately before the declaration.
			if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
				sb.Clear();
		}

		if (sb.Length > 0)
		{
			var stripped = DocTriviaStripPattern.Replace(sb.ToString(), "").Trim();
			if (stripped.Length > 0)
				return stripped;
		}

		return "";
	}

	private static string TryExtractFullDocumentationFromTrivia(IMethodSymbol method)
	{
		foreach (var sr in method.DeclaringSyntaxReferences)
		{
			if (sr.GetSyntax() is not MethodDeclarationSyntax m)
				continue;
			var result = ExtractDocumentationFromTriviaList(m.GetLeadingTrivia());
			if (result.Length > 0)
				return result;
		}

		return "";
	}

	private static string TryExtractFullDocumentationFromPropertyTrivia(IPropertySymbol prop)
	{
		foreach (var sr in prop.DeclaringSyntaxReferences)
		{
			switch (sr.GetSyntax())
			{
				case PropertyDeclarationSyntax p:
				{
					var result = ExtractDocumentationFromTriviaList(p.GetLeadingTrivia());
					if (result.Length > 0)
						return result;
					break;
				}
				// Positional record (and class primary-constructor) parameters: the public API is a property
				// whose declaring syntax is the parameter, not a property declaration.
				case ParameterSyntax par:
				{
					var result = ExtractDocumentationFromTriviaList(par.GetLeadingTrivia());
					if (result.Length > 0)
						return result;
					break;
				}
			}
		}

		return "";
	}

	private static string TryExtractDocumentationFromParameterTrivia(IParameterSymbol param)
	{
		foreach (var sr in param.DeclaringSyntaxReferences)
		{
			if (sr.GetSyntax() is not ParameterSyntax par)
				continue;
			var result = ExtractDocumentationFromTriviaList(par.GetLeadingTrivia());
			if (result.Length > 0)
				return result;
		}

		return "";
	}

	private static string TryExtractFullDocumentationFromFieldTrivia(IFieldSymbol field)
	{
		foreach (var sr in field.DeclaringSyntaxReferences)
		{
			switch (sr.GetSyntax())
			{
				case FieldDeclarationSyntax fd:
				{
					var result = ExtractDocumentationFromTriviaList(fd.GetLeadingTrivia());
					if (result.Length > 0)
						return result;
					break;
				}
				case VariableDeclaratorSyntax vd when vd.Parent is VariableDeclarationSyntax { Parent: FieldDeclarationSyntax fd }:
				{
					var result = ExtractDocumentationFromTriviaList(fd.GetLeadingTrivia());
					if (result.Length > 0)
						return result;
					break;
				}
			}
		}

		return "";
	}

	private static string TryExtractFullDocumentationFromTypeTrivia(INamedTypeSymbol type)
	{
		foreach (var sr in type.DeclaringSyntaxReferences)
		{
			if (sr.GetSyntax() is not BaseTypeDeclarationSyntax typeDecl)
				continue;
			var result = ExtractDocumentationFromTriviaList(typeDecl.GetLeadingTrivia());
			if (result.Length > 0)
				return result;
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
		var xml = type.GetDocumentationCommentXml();
		if (string.IsNullOrWhiteSpace(xml))
			xml = TryExtractFullDocumentationFromTypeTrivia(type);
		if (!string.IsNullOrWhiteSpace(xml))
		{
			var fromXml = Documentation.GetTypeSummaryLine(xml);
			if (!string.IsNullOrWhiteSpace(fromXml))
				return fromXml.Trim();
		}
		return "";
	}

	private static MethodDocumentation MergeMethodDocumentationFromTrivia(
		IMethodSymbol method,
		MethodDocumentation docs,
		CSharpParseOptions parseOptions)
	{
		// GetDocumentationCommentXml() is empty when GenerateDocumentationFile is not set.
		// In that case parse the full doc comment directly from syntax trivia so all fields
		// (summary, remarks, params, examples) are recovered without requiring that MSBuild property.
		if (string.IsNullOrWhiteSpace(docs.SummaryOneLiner))
		{
			var full = TryExtractFullDocumentationFromTrivia(method);
			if (full.Length > 0)
			{
				var fromTrivia = Documentation.ParseMethod(full, parseOptions);
				if (!string.IsNullOrWhiteSpace(fromTrivia.SummaryOneLiner))
					return fromTrivia;
			}
		}

		return docs;
	}

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

		public static string GetParamDocFromType(INamedTypeSymbol type, string parameterName, Compilation? compilation = null, string? fallbackXml = null)
		{
			var xml = type.GetDocumentationCommentXml();
			if (string.IsNullOrWhiteSpace(xml))
				xml = GetDocumentationXmlFromMetadataReference(type, compilation);
			if (string.IsNullOrWhiteSpace(xml))
				xml = fallbackXml;
			return GetParamDocFromXmlFragment(xml, parameterName);
		}

		/// <summary>Extracts <c>&lt;param name="…"&gt;</c> text from documentation XML (handles <c>&lt;member&gt;</c>-wrapped compiler output).</summary>
		public static string GetParamDocFromXmlFragment(string? xml, string parameterName)
		{
			if (string.IsNullOrWhiteSpace(xml))
				return "";
			try
			{
				var doc = XDocument.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
				var root = doc.Root;
				if (root is null)
					return "";
				foreach (var pe in root.Descendants())
				{
					if (!string.Equals(pe.Name.LocalName, "param", StringComparison.Ordinal))
						continue;
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

		public static string GetPropertySummaryLine(IPropertySymbol prop, Compilation? compilation = null, string? fallbackXml = null)
		{
			var xml = prop.GetDocumentationCommentXml();
			if (string.IsNullOrWhiteSpace(xml))
				xml = GetDocumentationXmlFromMetadataReference(prop, compilation);
			if (string.IsNullOrWhiteSpace(xml))
				xml = fallbackXml;
			// Compiler / GetDocumentationCommentXml often wraps content in <member>; use the same
			// summary resolution as types (descendant <summary>) so help text is not dropped.
			return GetTypeSummaryLine(xml);
		}

		public static string GetFieldSummaryLine(IFieldSymbol field, Compilation? compilation = null, string? fallbackXml = null)
		{
			var xml = field.GetDocumentationCommentXml();
			if (string.IsNullOrWhiteSpace(xml))
				xml = GetDocumentationXmlFromMetadataReference(field, compilation);
			if (string.IsNullOrWhiteSpace(xml))
				xml = fallbackXml;
			return GetTypeSummaryLine(xml);
		}

		public static string GetDocumentationXmlFromMetadataReference(ISymbol symbol, Compilation? compilation, string? artifactsPath = null)
		{
			if (compilation is null)
				return "";
			var docId = symbol.GetDocumentationCommentId();
			if (string.IsNullOrWhiteSpace(docId))
				return "";
			var containingAssembly = symbol.ContainingAssembly;
			if (containingAssembly is null)
				return "";

#pragma warning disable RS1035 // Required to load companion XML docs for metadata references.
			foreach (var reference in compilation.References)
			{
				if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol referenceAssembly)
					continue;
				if (!SymbolEqualityComparer.Default.Equals(referenceAssembly, containingAssembly))
					continue;
				var referenceDisplay = reference.Display;
				if (string.IsNullOrWhiteSpace(referenceDisplay))
					continue;
				foreach (var xmlPath in GetXmlDocumentationCandidates(referenceDisplay!, containingAssembly.Name, artifactsPath))
				{
					if (!global::System.IO.File.Exists(xmlPath))
						continue;
					try
					{
						var doc = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
						var member = doc.Root?
							.Element("members")?
							.Elements("member")
							.FirstOrDefault(m => string.Equals(m.Attribute("name")?.Value, docId, StringComparison.Ordinal));
						if (member is not null)
							return string.Concat(member.Nodes().Select(n => n.ToString()));
					}
					catch
					{
						// ignore malformed external XML docs
					}
				}
			}
#pragma warning restore RS1035

			return "";
		}

		private static IEnumerable<string> GetXmlDocumentationCandidates(string referencePath, string assemblyName, string? artifactsPath = null)
		{
			var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			static string NormalizePathSeparators(string p) => p.Replace('\\', '/');

			var direct = global::System.IO.Path.ChangeExtension(referencePath, ".xml");
			if (!string.IsNullOrWhiteSpace(direct) && yielded.Add(direct))
				yield return direct;

			var referenceDir = global::System.IO.Path.GetDirectoryName(referencePath);
			if (!string.IsNullOrWhiteSpace(referenceDir))
			{
				var byAssemblyName = global::System.IO.Path.Combine(referenceDir!, assemblyName + ".xml");
				if (yielded.Add(byAssemblyName))
					yield return byAssemblyName;
				var leaf = global::System.IO.Path.GetFileName(referenceDir);
				if (string.Equals(leaf, "ref", StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(leaf, "refint", StringComparison.OrdinalIgnoreCase))
				{
					var parent = global::System.IO.Path.GetDirectoryName(referenceDir!);
					if (!string.IsNullOrWhiteSpace(parent))
					{
						var sibling = global::System.IO.Path.Combine(parent, assemblyName + ".xml");
						if (yielded.Add(sibling))
							yield return sibling;
					}
				}
			}

			var normalized = NormalizePathSeparators(referencePath);
			var objMarker = "/obj/";
			var idxObj = normalized.IndexOf(objMarker, StringComparison.OrdinalIgnoreCase);
			if (idxObj >= 0)
			{
				var binPath = normalized.Substring(0, idxObj) + "/bin/" + normalized.Substring(idxObj + objMarker.Length);
				binPath = binPath.Replace("/refint/", "/").Replace("/ref/", "/");
				var platformPath = binPath.Replace('/', global::System.IO.Path.DirectorySeparatorChar);
				var binXml = global::System.IO.Path.ChangeExtension(platformPath, ".xml");
				if (yielded.Add(binXml))
					yield return binXml;
			}

			// When <ArtifactsPath> is known, build the canonical bin/{Project}/{Pivot}/{Assembly}.xml
			// path even when the reference points to a ref/, refint/, or obj/ subdirectory.
			if (!string.IsNullOrWhiteSpace(artifactsPath))
			{
				var normalizedArtifacts = NormalizePathSeparators(artifactsPath!.TrimEnd('/', '\\'));
				if (normalized.Length > normalizedArtifacts.Length &&
				    normalized[normalizedArtifacts.Length] == '/' &&
				    normalized.StartsWith(normalizedArtifacts, StringComparison.OrdinalIgnoreCase))
				{
					var relPath = normalized.Substring(normalizedArtifacts.Length + 1);
					if (relPath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase))
						relPath = "bin/" + relPath.Substring(4);
					relPath = ReplaceOrdinalIgnoreCase(ReplaceOrdinalIgnoreCase(relPath, "/refint/", "/"), "/ref/", "/");
					var platformPath = (normalizedArtifacts + "/" + relPath)
						.Replace('/', global::System.IO.Path.DirectorySeparatorChar);
					var artifactXml = global::System.IO.Path.ChangeExtension(platformPath, ".xml");
					if (yielded.Add(artifactXml))
						yield return artifactXml;
				}
			}
		}

		private static string ReplaceOrdinalIgnoreCase(string input, string oldValue, string newValue)
		{
			var idx = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
			if (idx < 0) return input;
			return input.Substring(0, idx) + newValue + input.Substring(idx + oldValue.Length);
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

}
