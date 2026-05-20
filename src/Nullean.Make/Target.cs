namespace Nullean.Make;

/// <summary>Fluent builder surface for plain targets (no typed per-target arguments).</summary>
public interface ITargetBuilder
{
	/// <summary>Overrides the CLI name (default: PascalCase property name → kebab-case).</summary>
	ITargetBuilder Named(string cliName);

	/// <summary>Sets the one-line description shown in help output.</summary>
	ITargetBuilder Description(string text);

	/// <summary>Hides this target from <c>--help</c> output and autocomplete. The target still runs correctly.</summary>
	ITargetBuilder Hidden();

	/// <summary>Skips the target body (but not its dependents) when the predicate returns <see langword="false"/>.</summary>
	ITargetBuilder OnlyWhen(Func<bool> condition);

	/// <summary>Declares targets that must succeed before this target's body runs. Skipped under <c>-s</c>.</summary>
	ITargetBuilder DependsOn(params TargetRef[] refs);

	/// <summary>Executes the given action as this target's body.</summary>
	ITargetBuilder Executes(Action body);

	/// <summary>Executes the given async delegate as this target's body.</summary>
	ITargetBuilder Executes(Func<Task> body);
}

/// <summary>
/// Declares a build target with no per-target CLI arguments.
/// Assign from a lambda: <c>public Target Clean =&gt; _ =&gt; _.Description("...").Executes(() =&gt; { });</c>
/// </summary>
public delegate ITargetBuilder Target(ITargetBuilder builder);
