using Nullean.Argh.Middleware;

namespace Nullean.Argh;

public sealed partial class ArghApp
{
	/// <summary>
	/// Registers global inline middleware. Call sites are analyzed by the source generator; this method is a no-op at runtime.
	/// </summary>
	/// <remarks>
	/// The generator will emit a middleware pipeline that invokes registered middleware around command execution.
	/// </remarks>
	public ArghApp UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware) => this;

	/// <summary>
	/// Registers a global middleware type. Call sites are analyzed by the source generator; this method is a no-op at runtime.
	/// </summary>
	/// <remarks>
	/// The generator will wire <typeparamref name="TMiddleware"/> into the middleware pipeline (and in hosting scenarios, types may be resolved from DI).
	/// </remarks>
	public ArghApp UseMiddleware<TMiddleware>() where TMiddleware : ICommandMiddleware => this;
}
