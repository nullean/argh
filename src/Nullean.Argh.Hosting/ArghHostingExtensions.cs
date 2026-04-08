using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nullean.Argh;

namespace Nullean.Argh.Hosting;

/// <summary>
/// Registers Argh CLI integration with the generic host.
/// </summary>
public static class ArghHostingExtensions
{
	/// <summary>
	/// Records the in-memory <see cref="ArghApp"/> model for source generation (analyzed at compile time). Does not register a hosted CLI runner.
	/// </summary>
	public static IServiceCollection AddArgh(this IServiceCollection services, string[] args, Action<ArghApp> configure)
	{
		if (services is null)
			throw new ArgumentNullException(nameof(services));
		if (configure is null)
			throw new ArgumentNullException(nameof(configure));

		_ = args;
		configure(new ArghApp());
		return services;
	}

	/// <summary>
	/// Records the <see cref="ArghApp"/> model, registers <see cref="ArghCliHostContext"/>, and adds a hosted service that
	/// invokes <paramref name="runCliAsync"/> (typically <c>() =&gt; ArghGenerated.RunAsync(args)</c>). When the CLI task completes,
	/// <see cref="IHostApplicationLifetime.StopApplication"/> is called so the process can exit.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="args">Arguments for source analysis and for forwarding to the generated runner.</param>
	/// <param name="configure">Fluent registration; must mirror commands passed to the generated <c>ArghGenerated</c> entry.</param>
	/// <param name="runCliAsync">Delegates to the app assembly’s generated <c>ArghGenerated.RunAsync</c> (or a test substitute).</param>
	/// <remarks>
	/// <para>
	/// Requires a generic host (e.g. <c>Host.CreateApplicationBuilder</c>) so <see cref="IHostApplicationLifetime"/> is available.
	/// </para>
	/// <para>
	/// Command <see cref="System.Threading.CancellationToken"/> parameters in generated code are still driven by console cancellation
	/// unless the generator is updated to prefer a host token; <see cref="ArghCliHostContext.ApplicationStopping"/> exposes the host
	/// shutdown token for application-level use until that unification exists.
	/// </para>
	/// </remarks>
	public static IServiceCollection AddArgh(
		this IServiceCollection services,
		string[] args,
		Action<ArghApp> configure,
		Func<Task<int>> runCliAsync)
	{
		if (services is null)
			throw new ArgumentNullException(nameof(services));
		if (configure is null)
			throw new ArgumentNullException(nameof(configure));
		if (runCliAsync is null)
			throw new ArgumentNullException(nameof(runCliAsync));

		configure(new ArghApp());

		services.AddSingleton(sp => new ArghCliHostContext(args, sp.GetRequiredService<IHostApplicationLifetime>()));
		services.AddSingleton<IHostedService>(sp => new ArghCliHostedService(
			runCliAsync,
			sp.GetRequiredService<IHostApplicationLifetime>(),
			sp.GetService<ILogger<ArghCliHostedService>>()));

		return services;
	}
}
