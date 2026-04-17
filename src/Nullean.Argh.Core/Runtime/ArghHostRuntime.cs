namespace Nullean.Argh.Runtime;

/// <summary>
/// Host integration for the source-generated CLI entry. Set by <c>Nullean.Argh.Hosting</c> for the duration of a hosted run.
/// </summary>
/// <remarks>
/// This type lives in <c>Nullean.Argh.Runtime</c> (not <c>Nullean.Argh.Hosting</c>) because source-generated <c>ArghGenerated</c> references it
/// without requiring a package reference to Hosting. The Hosting package assigns <see cref="ApplicationStopping"/> during a hosted run.
/// </remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class ArghHostRuntime
{
	/// <summary>
	/// When set, the generated CLI runner links this token with console cancellation. Cleared after the CLI task completes.
	/// </summary>
	public static CancellationToken? ApplicationStopping { get; set; }
}

/// <summary>
/// Dependency injection hook for generated command and middleware instantiation. Set by the generic host immediately before the generated CLI run.
/// </summary>
/// <remarks>
/// <para>
/// For <c>app.Map&lt;T&gt;()</c> instance methods and <c>UseMiddleware&lt;T&gt;()</c> / <c>[Middleware&lt;T&gt;]</c>, generated code resolves
/// <c>T</c> with <c>GetService(typeof(T))</c> (non-generic) when this provider is non-null; otherwise it uses <c>new T()</c>.
/// </para>
/// <para>
/// Native AOT / trimming: register command and middleware types in DI so required constructors are preserved; non-registered types fall back to parameterless construction.
/// </para>
/// </remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class ArghServices
{
	/// <summary>Root service provider for the current CLI invocation, or <c>null</c> when not running under a configured host. Uses BCL <see cref="System.IServiceProvider"/> so the base package does not reference Microsoft.Extensions.*; hosts (e.g. generic host) assign a provider that implements this interface.</summary>
	public static System.IServiceProvider? ServiceProvider { get; set; }
}
