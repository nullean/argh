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
	public static IServiceCollection AddArgh(this IServiceCollection services, string[] args, Action<IArghBuilder> configure)
	{
		if (services is null)
			throw new ArgumentNullException(nameof(services));
		if (configure is null)
			throw new ArgumentNullException(nameof(configure));

		_ = args;
		configure(new ArghBuilder());
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
	/// Command <see cref="System.Threading.CancellationToken"/> parameters use a token linked from console cancellation and
	/// <see cref="Nullean.Argh.ArghHostRuntime.ApplicationStopping"/> (set from <see cref="IHostApplicationLifetime.ApplicationStopping"/> for this run).
	/// </para>
	/// </remarks>
	public static IServiceCollection AddArgh(
		this IServiceCollection services,
		string[] args,
		Action<IArghBuilder> configure,
		Func<Task<int>> runCliAsync)
	{
		if (services is null)
			throw new ArgumentNullException(nameof(services));
		if (configure is null)
			throw new ArgumentNullException(nameof(configure));
		if (runCliAsync is null)
			throw new ArgumentNullException(nameof(runCliAsync));

		configure(new ArghBuilder());

		services.AddSingleton(sp => new ArghCliHostContext(args, sp.GetRequiredService<IHostApplicationLifetime>()));
		services.AddSingleton<IHostedService>(sp => new ArghCliHostedService(
			runCliAsync,
			sp.GetRequiredService<IHostApplicationLifetime>(),
			sp,
			sp.GetService<ILogger<ArghCliHostedService>>()));

		return services;
	}
}
