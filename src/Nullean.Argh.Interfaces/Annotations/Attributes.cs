namespace Nullean.Argh;

/// <summary>
/// On a handler type used with <c>MapNamespace&lt;T&gt;(...)</c>, names the CLI segment when using the
/// overload of <c>MapNamespace&lt;T&gt;</c> without a <c>string name</c> argument. At runtime, this attribute is required for that overload.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NamespaceSegmentAttribute : Attribute
{
	public NamespaceSegmentAttribute(string segment) => Segment = segment;

	/// <summary>Namespace path segment (e.g. <c>storage</c> for <c>app storage …</c>).</summary>
	public string Segment { get; }
}

/// <summary>
/// Marks a public method on a type registered with <c>Map&lt;T&gt;</c> or <c>MapNamespace&lt;T&gt;</c> as the
/// default handler when that scope is selected with no deeper subcommand (same role as <c>MapRoot</c> inside a namespace).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DefaultCommandAttribute : Attribute;

/// <summary>
/// Overrides the CLI command name for a method registered via <c>Map&lt;T&gt;</c> or <c>MapNamespace&lt;T&gt;</c>.
/// Without this attribute the name is derived automatically from the method name (PascalCase → kebab-case,
/// with <c>Async</c>/<c>Command</c>/<c>Handler</c> suffixes stripped).
/// Additional arguments are treated as command aliases (e.g. <c>[CommandName("my-command", "mc")]</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandNameAttribute : Attribute
{
	public CommandNameAttribute(string name, params string[] aliases)
	{
		Name = name;
		Aliases = aliases;
	}

	/// <summary>The kebab-case CLI name for this command (e.g. <c>"my-command"</c>).</summary>
	public string Name { get; }

	/// <summary>Optional additional names this command can be invoked by.</summary>
	public string[] Aliases { get; }
}

/// <summary>Marks a parameter as a positional CLI argument (successive positions starting at 0).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ArgumentAttribute : Attribute;

/// <summary>Specifies an <see cref="Nullean.Argh.Parsing.IArgumentParser{T}"/> implementation used to parse this parameter or property.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class ArgumentParserAttribute : Attribute
{
	public ArgumentParserAttribute(Type parserType) => ParserType = parserType;

	public Type ParserType { get; }
}

/// <summary>
/// When applied to a command parameter, binds public primary-constructor parameters and init-only properties
/// of the parameter type as CLI flags (or <see cref="ArgumentAttribute"/> positionals) instead of a single value.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class AsParametersAttribute : Attribute
{
	/// <summary>Optional kebab-case prefix applied to every generated long name (e.g. <c>app</c> → <c>--app-name</c>).</summary>
	public string? Prefix { get; }

	public AsParametersAttribute() => Prefix = null;

	public AsParametersAttribute(string prefix) => Prefix = prefix;
}

/// <summary>
/// Overrides parsing for collection-typed parameters. By default, values are collected from repeated flags
/// (<c>--tag a --tag b</c>). When <see cref="Separator"/> is set, a single flag value is split instead.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CollectionSyntaxAttribute : Attribute
{
	/// <summary>When non-null and non-empty, the flag value is split on this separator; otherwise repeated flags are used.</summary>
	public string? Separator { get; set; }
}

/// <summary>
/// Associates per-command middleware with a command method. The source generator reads this metadata; it has no runtime behavior until the generator emits the middleware pipeline.
/// </summary>
/// <typeparam name="TMiddleware">A type implementing <see cref="Nullean.Argh.Middleware.ICommandMiddleware"/>.</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MiddlewareAttribute<TMiddleware> : Attribute where TMiddleware : Nullean.Argh.Middleware.ICommandMiddleware;

/// <summary>
/// Restricts a <see cref="System.TimeSpan"/> to an inclusive range. Minimum and maximum use the same syntax as CLI <see cref="TimeSpan"/> binding (compact <c>Ns</c>/<c>Nm</c>/<c>Nh</c>/<c>Nd</c> or invariant <see cref="TimeSpan"/> text).
/// Only valid on <c>TimeSpan</c> or <c>TimeSpan?</c> parameters and properties.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class TimeSpanRangeAttribute : Attribute
{
	public TimeSpanRangeAttribute(string minimum, string maximum)
	{
		Minimum = minimum;
		Maximum = maximum;
	}

	public string Minimum { get; }

	public string Maximum { get; }
}

/// <summary>
/// Restricts the allowed URI schemes for a <see cref="System.Uri"/> parameter (e.g. <c>http</c>, <c>https</c>).
/// Emits a parse error when the provided URI does not use one of the listed schemes.
/// Only valid on <c>Uri</c> or <c>Uri?</c> parameters; applying it to other types produces diagnostic AGH0023.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class UriSchemeAttribute : Attribute
{
	public string[] Schemes { get; }

	public UriSchemeAttribute(params string[] schemes) => Schemes = schemes;
}

/// <summary>
/// Marks a command method as intrinsic — a command that returns information about the CLI itself
/// (e.g. version info, status, or diagnostic output) and does not perform application business logic.
/// <para>
/// When an intrinsic command is detected, <c>AddArgh</c> automatically suppresses host startup logs
/// below <c>LogLevel.Warning</c> so the command output is not polluted by infrastructure noise.
/// Override the threshold via <c>b.IntrinsicLogLevelMinimum(LogLevel)</c> on the hosting builder.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandIntrinsicAttribute : Attribute;

/// <summary>
/// Suppresses the AGH0021 diagnostic for this command method or handler class. Use when a command
/// intentionally does not need access to the registered <c>UseGlobalOptions</c> or namespace options types.
/// Globals and namespace-scoped options still parse (<c>-h</c>, shorts, longs) after route segments —
/// reconstructed instances are injected into middleware and Hosting; omitting eliminates only the handler parameter positions.
/// Apply to a <b>method</b> to opt out for that command only, or to a <b>class</b> to opt out for
/// all commands defined on that class.
/// Lambdas are always exempt from AGH0021 and do not need this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class NoOptionsInjectionAttribute : Attribute;

/// <summary>
/// Validates that the path refers to existing storage: <see cref="System.IO.FileInfo"/> → file must exist;
/// <see cref="System.IO.DirectoryInfo"/> → directory must exist.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class ExistingAttribute : Attribute;

/// <summary>
/// Validates that the path is unused: neither a file nor a directory may exist there.
/// Use with <see cref="System.IO.FileInfo"/> or <see cref="System.IO.DirectoryInfo"/>; checks are the same.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class NonExistingAttribute : Attribute;

/// <summary>Rejects symbolic links and other reparse points at the resolved path.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class RejectSymbolicLinksAttribute : Attribute;

/// <summary>
/// Expands <c>~/</c> / <c>~\</c> and bare <c>~</c> to the user profile directory, then resolves to a full path.
/// Only valid on <see cref="System.IO.FileInfo"/> or <see cref="System.IO.DirectoryInfo"/> parameters or properties.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class ExpandUserProfileAttribute : Attribute;

/// <summary>
/// Overrides the CLI string used to parse and display an enum member.
/// Without this attribute the CLI string is the member name lowercased (e.g. <c>MyValue</c> → <c>myvalue</c>).
/// The value is matched case-insensitively at parse time.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class EnumValueAttribute : Attribute
{
	public EnumValueAttribute(string value) => Value = value;

	/// <summary>The CLI string users type on the command line (e.g. <c>"fire-red"</c>).</summary>
	public string Value { get; }
}

/// <summary>
/// Marks a command method or parameter as hidden from user-facing help and autocomplete suggestions.
/// The command or parameter still parses and works correctly, and appears in <c>__schema</c> output
/// with <c>hidden: true</c> so tooling can suppress it selectively.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class HiddenAttribute : Attribute;

/// <summary>
/// Boolean side-effect flags for <see cref="CommandIntentAttribute"/>.
/// Combine with <c>|</c>: <c>[CommandIntent(CommandIntentFlags.Destructive | CommandIntentFlags.RequiresConfirmation)]</c>.
/// </summary>
[Flags]
public enum CommandIntentFlags
{
	/// <summary>No flags set.</summary>
	None = 0,
	/// <summary>The command deletes, overwrites, or irreversibly modifies data or resources.</summary>
	Destructive = 1 << 0,
	/// <summary>Calling the command multiple times produces the same result as calling it once.</summary>
	Idempotent = 1 << 1,
	/// <summary>The command blocks on an interactive stdin prompt when run without a <c>confirmationSkip</c>-role parameter.</summary>
	RequiresConfirmation = 1 << 2,
	/// <summary>This specific command requires an authenticated session.</summary>
	RequiresAuth = 1 << 3,
}

/// <summary>Blast radius of a command's side effects, used in <see cref="CommandIntentAttribute"/>.</summary>
public enum CommandScope
{
	/// <summary>Not declared.</summary>
	Unspecified = 0,
	/// <summary>Affects a single file.</summary>
	File,
	/// <summary>Affects a directory tree.</summary>
	Directory,
	/// <summary>Affects cloud resources, databases, registries, or other shared state.</summary>
	Global,
}

/// <summary>
/// Declares the side-effect profile of a command for agent-reasoning consumers.
/// Emitted as the <c>intent</c> object in <c>__schema</c> output.
/// </summary>
/// <example><code>
/// [CommandIntent(CommandIntentFlags.Destructive | CommandIntentFlags.RequiresConfirmation, Scope = CommandScope.Global)]
/// </code></example>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandIntentAttribute : Attribute
{
	public CommandIntentAttribute(CommandIntentFlags flags = CommandIntentFlags.None) => Flags = flags;

	/// <summary>Boolean side-effect flags.</summary>
	public CommandIntentFlags Flags { get; }

	/// <summary>Blast radius of the command's side effects.</summary>
	public CommandScope Scope { get; set; } = CommandScope.Unspecified;
}

/// <summary>
/// Declares the machine-readable output formats a command supports.
/// Emitted as the <c>output</c> object in <c>__schema</c> output.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandOutputAttribute : Attribute
{
	/// <summary>Supported output format names (e.g. <c>"json"</c>, <c>"table"</c>).</summary>
	public string[] Formats { get; }

	/// <summary>The flag name used to select the format (e.g. <c>"--output"</c>).</summary>
	public string? FormatFlag { get; set; }

	public CommandOutputAttribute(params string[] formats) => Formats = formats;
}

/// <summary>
/// Marks a boolean flag parameter as a confirmation-skip signal (e.g. <c>--yes</c>, <c>--force</c>).
/// Emits <c>role: "confirmationSkip"</c> in <c>__schema</c> so agent consumers know to pass this flag
/// automatically on destructive commands when running non-interactively.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class ConfirmationSkipAttribute : Attribute;

/// <summary>
/// Marks a boolean flag parameter as a dry-run signal (e.g. <c>--dry-run</c>, <c>--whatif</c>).
/// Emits <c>role: "dryRun"</c> in <c>__schema</c> so agent consumers know to pass this flag
/// when they want to preview effects without committing side effects.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class DryRunAttribute : Attribute;

/// <summary>Describes an environment variable the program reads. Used with <see cref="Nullean.Argh.Builder.IArghRootBuilder.DocumentEnvironmentVariables"/>.</summary>
public sealed class CliEnvVarDoc
{
	public CliEnvVarDoc(string name, string? description = null, bool required = false, string? defaultValue = null)
	{
		Name = name;
		Description = description;
		Required = required;
		DefaultValue = defaultValue;
	}

	public string Name { get; }
	public string? Description { get; }
	public bool Required { get; }
	public string? DefaultValue { get; }
}

/// <summary>Describes a configuration file the program reads. Used with <see cref="Nullean.Argh.Builder.IArghRootBuilder.DocumentEnvironmentVariables"/>.</summary>
public sealed class CliConfigFileDoc
{
	public CliConfigFileDoc(string path, string? description = null, bool required = false)
	{
		Path = path;
		Description = description;
		Required = required;
	}

	public string Path { get; }
	public string? Description { get; }
	public bool Required { get; }
}

