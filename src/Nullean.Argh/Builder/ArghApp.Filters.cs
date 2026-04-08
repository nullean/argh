using Nullean.Argh.Filters;

namespace Nullean.Argh.Builder;

public sealed partial class ArghApp
{
	/// <summary>
	/// Registers a global inline filter. Call sites are analyzed by the source generator; this method is a no-op at runtime.
	/// </summary>
	/// <remarks>
	/// The generator will emit a filter pipeline that invokes registered filters around command execution.
	/// </remarks>
	public ArghApp UseFilter(Func<CommandContext, CommandFilterDelegate, ValueTask> filter) => this;

	/// <summary>
	/// Registers a global filter type. Call sites are analyzed by the source generator; this method is a no-op at runtime.
	/// </summary>
	/// <remarks>
	/// The generator will wire <typeparamref name="TFilter"/> into the filter pipeline (and in hosting scenarios, types may be resolved from DI).
	/// </remarks>
	public ArghApp UseFilter<TFilter>() where TFilter : ICommandFilter => this;
}
