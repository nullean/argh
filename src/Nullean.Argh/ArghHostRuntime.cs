using System.Threading;

namespace Nullean.Argh;

/// <summary>
/// Host integration for the source-generated CLI entry. Set by <c>Nullean.Argh.Hosting</c> for the duration of a hosted run.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class ArghHostRuntime
{
	/// <summary>
	/// When set, the generated CLI runner links this token with console cancellation. Cleared after the CLI task completes.
	/// </summary>
	public static CancellationToken? ApplicationStopping { get; set; }
}

/// <summary>
/// Dependency injection hook for generated command and filter instantiation. Set by the generic host immediately before the generated CLI run.
/// </summary>
/// <remarks>
/// <para>
/// For <c>app.Add&lt;T&gt;()</c> instance methods and <c>UseFilter&lt;T&gt;()</c> / <c>[Filter&lt;T&gt;]</c>, generated code resolves
/// <c>T</c> with <c>GetService<c>T</c> with <c>GetService(typeof(T))</c> (non-generic) when this provider is non-null; otherwise it uses <c>new T()</c>.lt;T<c>T</c> with <c>GetService(typeof(T))</c> (non-generic) when this provider is non-null; otherwise it uses <c>new T()</c>.gt;()</c> when this provider is non-null; otherwise it uses <c>new T()</c>.
/// </para>
/// <para>
/// Native AOT / trimming: register command and filter types in DI so required constructors are preserved; non-registered types fall back to parameterless construction.
/// </para>
/// </remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class ArghServices
{
	/// <summary>Root service provider for the current CLI invocation, or <c>null</c> when not running under a configured host.</summary>
	public static IServiceProvider? ServiceProvider { get; set; }
}
