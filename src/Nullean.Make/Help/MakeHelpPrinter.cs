using Nullean.Argh;
using Nullean.Make.Discovery;
using Nullean.Make.Parsing;
using Nullean.Argh.Help;
using System.Reflection;

namespace Nullean.Make.Help;

internal static class MakeHelpPrinter
{
	public static void PrintRoot(BuildGraph graph, string scriptName)
	{
		Console.WriteLine();
		Console.WriteLine(CliHelpFormatting.Section(graph.AppName));
		Console.WriteLine();

		// Separate targets, commands, and namespaces
		var targets = graph.Targets.Where(t => !t.Hidden && t.Kind == TargetKind.Target && t.Route.Length == 1).ToList();
		var commands = graph.Targets.Where(t => !t.Hidden && t.Kind == TargetKind.Command && t.Route.Length == 1).ToList();
		var nsTargets = graph.Targets.Where(t => !t.Hidden && t.Route.Length > 1).ToList();
		var nsNames = nsTargets.Select(t => t.Route[0]).Distinct().OrderBy(x => x).ToList();

		Console.WriteLine($"  {CliHelpFormatting.Section("Usage:")}");
		Console.WriteLine($"    {scriptName} <target|command> [options]");
		if (nsNames.Count > 0)
			Console.WriteLine($"    {scriptName} <namespace> <target|command> [options]");
		Console.WriteLine();

		if (targets.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Targets:")}");
			var colWidth = targets.Max(t => t.Name.Length) + 2;
			foreach (var t in targets)
			{
				var deps = t.RequiresResolved.Concat(t.ComposesResolved).ToList();
				var depSuffix = deps.Count > 0
					? $"  (depends on: {string.Join(", ", deps.Select(d => string.Join(" ", d.Route)))})"
					: "";
				CliHelpFormatting.WriteHelpListNameAndDescription(false, t.Name, t.Description + depSuffix, colWidth);
			}
			Console.WriteLine();
		}

		if (commands.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Commands:")}");
			var colWidth = commands.Max(t => t.Name.Length) + 2;
			foreach (var c in commands)
				CliHelpFormatting.WriteHelpListNameAndDescription(true, c.Name, c.Description, colWidth);
			Console.WriteLine();
		}

		if (nsNames.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Namespaces:")}");
			var colWidth = nsNames.Max(n => n.Length) + 2;
			foreach (var ns in nsNames)
				CliHelpFormatting.WriteHelpListNameAndDescription(true, ns, null, colWidth);
			Console.WriteLine();
		}

		PrintGlobalOptions(graph, scriptName);
	}

	public static void PrintTarget(TargetNode node, BuildGraph graph, string scriptName)
	{
		Console.WriteLine();
		var route = string.Join(" ", node.Route);
		Console.WriteLine($"  {CliHelpFormatting.Accent(route)} — {node.Description ?? "(no description)"}");
		Console.WriteLine();

		Console.WriteLine($"  {CliHelpFormatting.Section("Usage:")}");
		var optionSuffix = node.DtoType is not null ? " [options]" : "";
		Console.WriteLine($"    {scriptName} {route}{optionSuffix}");
		Console.WriteLine();

		if (node.DtoType is not null)
		{
			PrintDtoOptions(node.DtoType);
			Console.WriteLine();
		}

		var allDeps = node.RequiresResolved.Concat(node.ComposesResolved).ToList();
		if (allDeps.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Depends on:")}");
			foreach (var dep in allDeps)
				Console.WriteLine($"    {string.Join(" ", dep.Route)}");
			Console.WriteLine();
		}

		PrintGlobalOptions(graph, scriptName);
	}

	public static void PrintCommand(TargetNode node, BuildGraph graph, string scriptName)
	{
		Console.WriteLine();
		var route = string.Join(" ", node.Route);
		Console.WriteLine($"  {CliHelpFormatting.Accent(route)} — {node.Description ?? "(no description)"}");
		Console.WriteLine();

		Console.WriteLine($"  {CliHelpFormatting.Section("Usage:")}");
		Console.WriteLine($"    {scriptName} {route} [-s] [global options]");
		Console.WriteLine();

		var num = 1;
		if (node.RequiresResolved.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Requires")} (skippable with -s):");
			foreach (var dep in node.RequiresResolved)
				Console.WriteLine($"    {num++,2}. {CliHelpFormatting.Placeholder(string.Join(" ", dep.Route))}");
			Console.WriteLine();
		}

		if (node.ComposesResolved.Count > 0)
		{
			Console.WriteLine($"  {CliHelpFormatting.Section("Composes")} (always runs):");
			foreach (var dep in node.ComposesResolved)
				Console.WriteLine($"    {num++,2}. {CliHelpFormatting.Placeholder(string.Join(" ", dep.Route))}");
			Console.WriteLine();
		}

		PrintGlobalOptions(graph, scriptName);
	}

	private static void PrintDtoOptions(Type dtoType)
	{
		Console.WriteLine($"  {CliHelpFormatting.Section("Options:")}");
		var ctor = dtoType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
		foreach (var p in ctor.GetParameters())
		{
			var isPositional = p.GetCustomAttribute<ArgumentAttribute>() is not null;
			var name = ToKebabCase(p.Name ?? "arg");
			var typeName = FriendlyTypeName(p.ParameterType);
			var defaultStr = p.HasDefaultValue ? $"  (default: {p.DefaultValue ?? "null"})" : "";

			if (isPositional)
				Console.WriteLine($"    {CliHelpFormatting.Placeholder($"<{name}>"),-30} {typeName}{defaultStr}");
			else
				Console.WriteLine($"    {CliHelpFormatting.Placeholder($"--{name} <{typeName}>"),-30}{defaultStr}");
		}
	}

	private static void PrintGlobalOptions(BuildGraph graph, string scriptName)
	{
		Console.WriteLine($"  {CliHelpFormatting.Section("Global options:")}");
		Console.WriteLine($"    {CliHelpFormatting.Placeholder("-s, --single-target"),-30}  Skip prerequisite deps; run only the body / Composes");
		Console.WriteLine($"    {CliHelpFormatting.Placeholder("-h, --help"),-30}  Show this help");

		foreach (var opt in graph.GlobalOptions)
		{
			var flags = opt.Short is not null ? $"{opt.Short}, {opt.Long}" : $"    {opt.Long}";
			var typeSuffix = opt.IsFlag ? "" : " <string>";
			Console.WriteLine($"    {CliHelpFormatting.Placeholder($"{flags}{typeSuffix}"),-30}  {opt.Description ?? ""}");
		}
		Console.WriteLine();
	}

	private static string ToKebabCase(string name)
	{
		var sb = new System.Text.StringBuilder();
		for (var i = 0; i < name.Length; i++)
		{
			var c = name[i];
			if (char.IsUpper(c) && i > 0) sb.Append('-');
			sb.Append(char.ToLowerInvariant(c));
		}
		return sb.ToString();
	}

	private static string FriendlyTypeName(Type t)
	{
		var u = Nullable.GetUnderlyingType(t) ?? t;
		return u.Name.ToLowerInvariant();
	}
}
