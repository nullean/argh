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
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandNameAttribute : Attribute
{
	public CommandNameAttribute(string name) => Name = name;

	/// <summary>The kebab-case CLI name for this command (e.g. <c>"my-command"</c>).</summary>
	public string Name { get; }
}

/// <summary>Marks a parameter as a positional CLI argument (successive positions starting at 0).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ArgumentAttribute : Attribute;

/// <summary>Specifies an <see cref="Nullean.Argh.Parsing.IArgumentParser{T}"/> implementation used to parse this parameter.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
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
/// Apply to a <b>method</b> to opt out for that command only, or to a <b>class</b> to opt out for
/// all commands defined on that class.
/// Lambdas are always exempt from AGH0021 and do not need this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class NoOptionsInjectionAttribute : Attribute;
