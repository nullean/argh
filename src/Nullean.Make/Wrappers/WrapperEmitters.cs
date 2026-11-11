using System.Text;
using Nullean.Make.Discovery;

namespace Nullean.Make.Wrappers;

/// <summary>Writes one wrapper's include file from a <see cref="WrapperModel"/>.</summary>
internal interface IWrapperEmitter
{
	/// <summary>The name <c>--only</c> takes: <c>mise</c>, <c>make</c> or <c>just</c>.</summary>
	string Key { get; }

	/// <summary>File name inside the output directory.</summary>
	string FileName { get; }

	/// <summary>Joins route segments (and the prefix) into a task name.</summary>
	string Separator { get; }

	string Emit(WrapperModel model);

	/// <summary>The lines to add to the wrapper's own config file to pull in <paramref name="relativePath"/>.</summary>
	string IncludeHint(string relativePath);
}

internal static class WrapperEmitters
{
	public static readonly IReadOnlyList<IWrapperEmitter> All = [new MiseEmitter(), new MakefileEmitter(), new JustEmitter()];
}

/// <summary>
/// A <c>task_config.includes</c> file: one task per target, top-level tables as mise expects in an include.
/// <c>raw_args = true</c> forwards every argument to Make untouched (a <c>usage</c> spec alone makes mise drop them),
/// and the <c>usage</c> spec still drives <c>mise</c> completions. No <c>depends</c>: Make runs the dependencies.
/// </summary>
internal sealed class MiseEmitter : IWrapperEmitter
{
	public string Key => "mise";
	public string FileName => "mise-tasks.toml";
	public string Separator => ":";

	public string Emit(WrapperModel model)
	{
		var sb = new StringBuilder(model.Header());
		foreach (var t in model.Targets)
		{
			sb.Append('\n');
			sb.Append('[').Append(Toml.Basic(model.NameOf(t, Separator))).Append("]\n");
			if (t.Description is not null)
				sb.Append("description = ").Append(Toml.Basic(t.Description)).Append('\n');
			sb.Append("raw_args = true\n");
			sb.Append("run = ").Append(Toml.Basic(model.CommandFor(t))).Append('\n');
			sb.Append("usage = '''\n");
			foreach (var line in UsageLines(t, model.Options))
				sb.Append(Toml.InLiteralMultiline(line)).Append('\n');
			sb.Append("'''\n");
		}
		return sb.ToString();
	}

	public string IncludeHint(string relativePath) => $"[task_config]\nincludes = [{Toml.Basic(relativePath)}]";

	private static IEnumerable<string> UsageLines(WrapperTarget t, IReadOnlyList<WrapperOption> shared)
	{
		foreach (var o in t.Options)
		{
			var help = Kdl.String(DtoHelp(o));
			if (o.IsRest)
				yield return $"arg {Kdl.String($"[{o.Name}]")} var=#true help={Kdl.String($"{DtoOptions.FriendlyTypeName(o.Type)} (everything after --)")}";
			else if (o.IsPositional)
				yield return IsList(o.Type) ? $"arg {Kdl.String($"[{o.Name}]")} var=#true help={help}" : $"arg {Kdl.String($"[{o.Name}]")} help={help}";
			else if (o.IsSwitch)
				yield return $"flag {Kdl.String($"--{o.Name}")} help={help}";
			else
				yield return $"flag {Kdl.String($"--{o.Name} <{o.Name}>")} help={help}";
		}
		foreach (var o in shared)
		{
			var names = o.Short is null ? o.Long : $"{o.Short} {o.Long}";
			var spec = o.IsFlag ? names : $"{names} <{o.Long.TrimStart('-')}>";
			var help = o.Description is null ? "" : $" help={Kdl.String(o.Description)}";
			yield return $"flag {Kdl.String(spec)}{help}";
		}
	}

	private static bool IsList(Type t) => t.IsGenericType && t.GetGenericTypeDefinition().Name == "FSharpList`1";

	private static string DtoHelp(DtoOption o)
	{
		var type = DtoOptions.FriendlyTypeName(o.Type);
		return o.HasDefault ? $"{type} (default: {o.Default ?? "null"})" : type;
	}
}

/// <summary>
/// A GNU make include: one phony target per Make target, arguments passed as <c>ARGS="..."</c>. It restores
/// <c>.DEFAULT_GOAL</c> at the end, so including it at the top of a Makefile does not make <c>make</c> run its first target.
/// </summary>
internal sealed class MakefileEmitter : IWrapperEmitter
{
	public string Key => "make";
	public string FileName => "make.mk";
	public string Separator => "/";

	public string Emit(WrapperModel model)
	{
		var sb = new StringBuilder(model.Header());
		sb.Append("#\n# make <target> ARGS=\"--flag value\"\n\n");
		// make expands $ and starts a comment at #.
		sb.Append("NULLEAN_MAKE_RUN ?= ").Append(model.Run.Replace("$", "$$").Replace("#", "\\#")).Append('\n');
		sb.Append("_nullean_make_default_goal := $(.DEFAULT_GOAL)\n\n");
		sb.Append(".PHONY:");
		foreach (var t in model.Targets)
			sb.Append(' ').Append(model.NameOf(t, Separator));
		sb.Append('\n');
		foreach (var t in model.Targets)
		{
			sb.Append('\n');
			if (t.Description is not null)
				sb.Append("# ").Append(t.Description).Append('\n');
			sb.Append(model.NameOf(t, Separator)).Append(":\n");
			sb.Append("\t@$(NULLEAN_MAKE_RUN) ").Append(string.Join(" ", t.Route)).Append(" $(ARGS)\n");
		}
		sb.Append("\n.DEFAULT_GOAL := $(_nullean_make_default_goal)\n");
		return sb.ToString();
	}

	public string IncludeHint(string relativePath) => $"-include {relativePath}";
}

/// <summary>
/// A justfile for <c>import?</c>: one recipe per target, arguments forwarded as positional arguments so quoting survives.
/// </summary>
internal sealed class JustEmitter : IWrapperEmitter
{
	public string Key => "just";
	public string FileName => "make.just";
	public string Separator => "-";

	public string Emit(WrapperModel model)
	{
		var sb = new StringBuilder(model.Header());
		foreach (var t in model.Targets)
		{
			sb.Append('\n');
			if (t.Description is not null)
				sb.Append("# ").Append(t.Description).Append('\n');
			sb.Append("[positional-arguments]\n");
			sb.Append('@').Append(model.NameOf(t, Separator)).Append(" *args:\n");
			// just interpolates {{ }}; {{{{ is a literal {{.
			sb.Append("    ").Append(model.CommandFor(t).Replace("{{", "{{{{")).Append(" \"$@\"\n");
		}
		return sb.ToString();
	}

	public string IncludeHint(string relativePath) => $"import? '{relativePath}'";
}

internal static class Toml
{
	/// <summary>A TOML basic string, quoted and escaped.</summary>
	public static string Basic(string value)
	{
		var sb = new StringBuilder("\"");
		foreach (var c in value)
		{
			switch (c)
			{
				case '\\':
					sb.Append("\\\\");
					break;
				case '"':
					sb.Append("\\\"");
					break;
				case '\n':
					sb.Append("\\n");
					break;
				case '\r':
					sb.Append("\\r");
					break;
				case '\t':
					sb.Append("\\t");
					break;
				default:
					if (char.IsControl(c))
						sb.Append($"\\u{(int)c:X4}");
					else
						sb.Append(c);
					break;
			}
		}
		return sb.Append('"').ToString();
	}

	/// <summary>Makes a line safe inside a <c>'''</c> literal string, which has no escapes and ends at <c>'''</c>.</summary>
	public static string InLiteralMultiline(string line)
	{
		while (line.Contains("'''"))
			line = line.Replace("'''", "''");
		return line;
	}
}

internal static class Kdl
{
	/// <summary>A KDL string, quoted and escaped.</summary>
	public static string String(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
