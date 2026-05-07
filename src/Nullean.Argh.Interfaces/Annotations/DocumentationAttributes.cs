namespace Nullean.Argh.Documentation;

/// <summary>
/// Boolean side-effect flags for <see cref="CommandIntentAttribute"/>.
/// Combine with <c>|</c>: <c>[CommandIntent(Intent.Destructive | Intent.RequiresConfirmation)]</c>.
/// </summary>
[Flags]
public enum Intent
{
	/// <summary>No flags set.</summary>
	None = 0,
	/// <summary>The command deletes, overwrites, or irreversibly modifies data or resources.</summary>
	Destructive = 1 << 0,
	/// <summary>Calling the command multiple times produces the same result as calling it once.</summary>
	Idempotent = 1 << 1,
	/// <summary>The command blocks on an interactive stdin prompt when run without a <c>confirmationSkip</c>-role parameter.</summary>
	RequiresConfirmation = 1 << 2,
}

/// <summary>
/// Mutation scope of a command — declares the blast radius of its side effects.
/// Used with <see cref="MutationScopeAttribute"/>.
/// </summary>
public enum MutationScope
{
	/// <summary>Affects a single file or path.</summary>
	File,
	/// <summary>Affects a directory tree.</summary>
	Directory,
	/// <summary>
	/// Reaches beyond the local filesystem — e.g. writes to a cloud service, database, registry,
	/// message queue, or any shared remote state.
	/// </summary>
	Global,
}

/// <summary>
/// Declares the side-effect profile of a command for agent-reasoning consumers.
/// Emitted as the <c>intent</c> object in <c>__schema</c> output.
/// Has no effect on parsing or validation.
/// </summary>
/// <example><code>
/// [CommandIntent(Intent.Destructive | Intent.RequiresConfirmation)]
/// [MutationScope(MutationScope.Global)]
/// [RequiresAuth]
/// public static Task Delete([ConfirmationSkip] bool yes = false) { ... }
/// </code></example>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandIntentAttribute : Attribute
{
	public CommandIntentAttribute(Intent flags = Intent.None) => Flags = flags;

	/// <summary>Boolean side-effect flags (combine with <c>|</c>).</summary>
	public Intent Flags { get; }
}

/// <summary>
/// Declares the mutation scope of a command — the blast radius of its side effects.
/// Emitted as <c>intent.scope</c> in <c>__schema</c> output.
/// Has no effect on parsing or validation.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><see cref="MutationScope.File"/> — affects a single file or path.</description></item>
/// <item><description><see cref="MutationScope.Directory"/> — affects a directory tree.</description></item>
/// <item><description><see cref="MutationScope.Global"/> — reaches beyond the local filesystem
/// (cloud resources, databases, registries, network services, etc.).</description></item>
/// </list>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MutationScopeAttribute : Attribute
{
	public MutationScopeAttribute(MutationScope scope) => Scope = scope;

	/// <summary>The mutation scope of the command.</summary>
	public MutationScope Scope { get; }
}

/// <summary>
/// Marks a command as requiring an authenticated session.
/// Emitted as <c>intent.requiresAuth: true</c> in <c>__schema</c> output.
/// Has no effect on parsing or validation.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresAuthAttribute : Attribute;

/// <summary>
/// Marks a parameter or property as a confirmation-skip signal (e.g. <c>--yes</c>, <c>--force</c>).
/// Emits <c>role: "confirmationSkip"</c> in <c>__schema</c> so agent consumers know to pass this flag
/// automatically on destructive commands when running non-interactively.
/// Has no effect on parsing or validation.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class ConfirmationSkipAttribute : Attribute;

/// <summary>
/// Marks a parameter or property as a dry-run signal (e.g. <c>--dry-run</c>, <c>--whatif</c>).
/// Emits <c>role: "dryRun"</c> in <c>__schema</c> so agent consumers know to pass this flag
/// when they want to preview effects without committing side effects.
/// Has no effect on parsing or validation.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class DryRunAttribute : Attribute;

/// <summary>
/// Marks a parameter or property as the command's output-format selector.
/// Emits the <c>output</c> object in <c>__schema</c>, with <c>formatFlag</c> derived from the
/// parameter's CLI name and <c>formats</c> from the enum's CLI values (or the explicit list).
/// Has no effect on parsing or validation.
/// </summary>
/// <remarks>
/// Apply to an enum-typed parameter or property to infer formats automatically:
/// <code>public static void Report([CommandOutput] OutputFormat? format = null) { ... }</code>
///
/// Or supply an explicit list for string-typed parameters:
/// <code>public static void Report([CommandOutput("json", "table")] string? format = null) { ... }</code>
///
/// Also works on <c>[AsParameters]</c> DTO properties and on GlobalOptions / NamespaceOptions properties.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class CommandOutputAttribute : Attribute
{
	/// <summary>Explicit output format names. When empty and the parameter is enum-typed, formats are inferred from the enum's CLI values.</summary>
	public string[] Formats { get; }

	public CommandOutputAttribute(params string[] formats) => Formats = formats;
}

/// <summary>Describes an environment variable the program reads. Used with <see cref="Nullean.Argh.Builder.IArghRootBuilder.DocumentEnvironmentVariables"/>.</summary>
public sealed class CliEnvVar
{
	public CliEnvVar(string name, string? description = null, bool required = false, string? defaultValue = null)
	{
		Name = name;
		Description = description;
		Required = required;
		DefaultValue = defaultValue;
	}

	/// <summary>Variable name (e.g. <c>"GITHUB_TOKEN"</c>).</summary>
	public string Name { get; }
	/// <summary>What the variable controls.</summary>
	public string? Description { get; }
	/// <summary>Whether the program requires this variable to function.</summary>
	public bool Required { get; }
	/// <summary>Default value if not set.</summary>
	public string? DefaultValue { get; }
}

/// <summary>Describes a configuration file the program reads. Used with <see cref="Nullean.Argh.Builder.IArghRootBuilder.DocumentEnvironmentVariables"/>.</summary>
public sealed class CliConfigFile
{
	public CliConfigFile(string path, string? description = null, bool required = false)
	{
		Path = path;
		Description = description;
		Required = required;
	}

	/// <summary>File path (<c>~</c> is expanded to the user's home directory).</summary>
	public string Path { get; }
	/// <summary>What the file controls.</summary>
	public string? Description { get; }
	/// <summary>Whether the program requires this file to exist.</summary>
	public bool Required { get; }
}
