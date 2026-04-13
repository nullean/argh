using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nullean.Argh.Runtime;

namespace Nullean.Argh.Hosting;

/// <summary>
/// Registers Argh CLI integration with the generic host.
/// </summary>
public static class ArghHostingExtensions
{
	/// <summary>
	/// Records the <see cref="ArghApp"/> model, registers <see cref="ArghHostingContext"/>, and adds a hosted service that
	/// invokes <see cref="ArghRuntime.RunAsync"/> for the application assembly (registered by the source generator).
	/// After the CLI task completes, the process terminates via <see cref="Environment.Exit(int)"/> so host lifetime output stays out of the way.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="args">Arguments for source analysis and for forwarding to the generated runner.</param>
	/// <param name="configure">Fluent registration; must mirror commands passed to the generated <c>ArghGenerated</c> entry.</param>
	/// <remarks>
	/// <para>
	/// Requires a generic host (e.g. <c>Host.CreateApplicationBuilder</c>) so <see cref="IHostApplicationLifetime"/> is available.
	/// </para>
	/// <para>
	/// Command <see cref="System.Threading.CancellationToken"/> parameters use a token linked from console cancellation and
	/// <see cref="Nullean.Argh.Runtime.ArghHostRuntime.ApplicationStopping"/> (set from <see cref="IHostApplicationLifetime.ApplicationStopping"/> for this run).
	/// </para>
	/// <para>
	/// <see cref="IHostedService.StartAsync"/> runs in registration order. Prefer calling <c>AddArgh</c> <em>before</em> other
	/// <see cref="IHostedService"/> / <see cref="BackgroundService"/> registrations so the CLI (including <c>--help</c>) runs first and
	/// can exit without starting additional hosted work. Any hosted service registered <em>before</em> <c>AddArgh</c> still runs
	/// <c>StartAsync</c> first on every invocation, including help; any hosted service registered <em>after</em> <c>AddArgh</c> never
	/// runs because the process exits after the CLI. <see cref="Environment.Exit(int)"/> does not run <c>StopAsync</c> on services that
	/// did start.
	/// </para>
	/// </remarks>
	public static IServiceCollection AddArgh(this IServiceCollection services, string[] args, Action<IArghHostingBuilder> configure)
	{
		if (services is null)
			throw new ArgumentNullException(nameof(services));
		if (configure is null)
			throw new ArgumentNullException(nameof(configure));

		configure(new ArghHostingBuilder(services));

		services.AddSingleton(sp => new ArghHostingContext(args, sp.GetRequiredService<IHostApplicationLifetime>()));
		services.AddSingleton<IHostedService>(sp => new ArghHostingCliService(
			() => ArghRuntime.RunAsync(args),
			sp.GetRequiredService<IHostApplicationLifetime>(),
			sp,
			sp.GetService<ILogger<ArghHostingCliService>>())
		);

		return services;
	}

	/// <summary>
	/// Records the <see cref="ArghApp"/> model, registers <see cref="ArghHostingContext"/>, and adds a hosted service that
	/// invokes <paramref name="runCliAsync"/>. When the CLI task completes, the process exits via <see cref="Environment.Exit(int)"/>.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="args">Arguments for source analysis and for forwarding to the runner.</param>
	/// <param name="configure">Fluent registration; must mirror commands passed to the generated <c>ArghGenerated</c> entry.</param>
	/// <param name="runCliAsync">Custom CLI entry (e.g. tests); prefer the overload without this parameter that uses <see cref="ArghRuntime.RunAsync"/>.</param>
	/// <remarks>
	/// <para>
	/// Requires a generic host (e.g. <c>Host.CreateApplicationBuilder</c>) so <see cref="IHostApplicationLifetime"/> is available.
	/// </para>
	/// <para>
	/// Command <see cref="System.Threading.CancellationToken"/> parameters use a token linked from console cancellation and
	/// <see cref="Nullean.Argh.Runtime.ArghHostRuntime.ApplicationStopping"/> (set from <see cref="IHostApplicationLifetime.ApplicationStopping"/> for this run).
	/// </para>
	/// <para>
	/// <see cref="IHostedService.StartAsync"/> runs in registration order. Prefer calling <c>AddArgh</c> <em>before</em> other
	/// <see cref="IHostedService"/> / <see cref="BackgroundService"/> registrations so the CLI (including <c>--help</c>) runs first and
	/// can exit without starting additional hosted work. Any hosted service registered <em>before</em> <c>AddArgh</c> still runs
	/// <c>StartAsync</c> first on every invocation, including help; any hosted service registered <em>after</em> <c>AddArgh</c> never
	/// runs because the process exits after the CLI. <see cref="Environment.Exit(int)"/> does not run <c>StopAsync</c> on services that
	/// did start.
	/// </para>
	/// </remarks>
	public static IServiceCollection AddArgh(
		this IServiceCollection services,
		string[] args,
		Action<IArghHostingBuilder> configure,
		Func<Task<int>> runCliAsync
	)
	{
		if (services is null)
			throw new ArgumentNullException(nameof(services));
		if (configure is null)
			throw new ArgumentNullException(nameof(configure));
		if (runCliAsync is null)
			throw new ArgumentNullException(nameof(runCliAsync));

		configure(new ArghHostingBuilder(services));

		services.AddSingleton(sp => new ArghHostingContext(args, sp.GetRequiredService<IHostApplicationLifetime>()));
		services.AddSingleton<IHostedService>(sp => new ArghHostingCliService(
			runCliAsync,
			sp.GetRequiredService<IHostApplicationLifetime>(),
			sp,
			sp.GetService<ILogger<ArghHostingCliService>>())
		);

		return services;
	}
}
