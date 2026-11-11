using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nullean.Make.Docs;

/// <summary>
/// Reads <c>&lt;summary&gt;</c> text from the XML documentation file that sits next to an assembly
/// (<c>MyBuild.dll</c> → <c>MyBuild.xml</c>), so a target's description can come from its doc comment.
/// The file exists only when the project sets <c>GenerateDocumentationFile</c>; without it every lookup returns null.
/// </summary>
internal static class DocDescriptions
{
	private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, string>> Cache = new();

	/// <summary>The summary for a type, such as a C# union case record (<c>T:Clean</c>).</summary>
	public static string? ForType(Type type) => type.FullName is { } name ? Summary(type.Assembly, "T:" + name.Replace('+', '.')) : null;

	/// <summary>The summary for a documentation id such as <c>T:My.Build.Target.Clean</c> (an F# union case).</summary>
	public static string? Summary(Assembly assembly, string docId) => Cache.GetOrAdd(assembly, Load).TryGetValue(docId, out var summary) ? summary : null;

	private static IReadOnlyDictionary<string, string> Load(Assembly assembly)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			if (string.IsNullOrEmpty(assembly.Location))
				return result;
			var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
			if (!File.Exists(xmlPath))
				return result;
			var doc = XDocument.Load(xmlPath);
			foreach (var member in doc.Descendants("member"))
			{
				var id = member.Attribute("name")?.Value;
				var summary = member.Element("summary");
				if (id is null || summary is null)
					continue;
				var text = Regex.Replace(string.Concat(summary.Nodes().Select(Flatten)), @"\s+", " ").Trim();
				if (text.Length > 0)
					result[id] = text;
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
		{
			// Docs are a nicety: an unreadable file means no descriptions, not a failed build.
		}
		return result;
	}

	/// <summary>Text of a doc node; <c>&lt;see cref="T:X"/&gt;</c> and <c>&lt;c&gt;</c> render as their names.</summary>
	private static string Flatten(XNode node) => node switch
	{
		XText t => t.Value,
		XElement { Name.LocalName: "see" or "seealso" } e => SeeText(e),
		XElement { Name.LocalName: "paramref" or "typeparamref" } e => e.Attribute("name")?.Value ?? "",
		XElement e => string.Concat(e.Nodes().Select(Flatten)),
		_ => ""
	};

	private static string SeeText(XElement see)
	{
		if (see.Attribute("langword")?.Value is { } langword)
			return langword;
		if (see.Attribute("cref")?.Value is { } cref)
			return cref[(cref.IndexOf(':') + 1)..].Split('.')[^1];
		return see.Value;
	}
}
