using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nullean.Argh.Runtime;

namespace Nullean.Argh.Hosting;

/// <summary>
/// Runs the CLI entry (<see cref="ArghRuntime.RunAsync"/> or a substitute supplied to <c>AddArgh</c>) in
/// <see cref="IHostedService.StartAsync"/>, then ends the process with <see cref="Environment.Exit(int)"/> so generic-host lifetime
/// / shutdown messages do not appear after the CLI run.
/// </summary>
/// <remarks>
/// Hosted services registered after <c>AddArgh</c> never get <see cref="IHostedService.StartAsync"/> because the process exits.
/// Register <c>AddArgh</c> before other hosted services so the CLI (including <c>--help</c>) runs first. Services registered before
/// <c>AddArgh</c> still start first. <see cref="Environment.Exit(int)"/> does not invoke <see cref="IHostedService.StopAsync"/>.
/// </remarks>
internal sealed class ArghHostingCliService : IHostedService
{
	private readonly Func<Task<int>> _runCliAsync;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly IServiceProvider _services;
	private readonly ILogger<ArghHostingCliService>? _logger;

	public ArghHostingCliService(
		Func<Task<int>> runCliAsync,
		IHostApplicationLifetime lifetime,
		IServiceProvider services,
		ILogger<ArghHostingCliService>? logger = null)
	{
		_runCliAsync = runCliAsync ?? throw new ArgumentNullException(nameof(runCliAsync));
		_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
		_services = services ?? throw new ArgumentNullException(nameof(services));
		_logger = logger;
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		// Run during IHostedService startup (before ApplicationStarted) so help and normal commands run without host "started" noise first.
		ArghHostRuntime.ApplicationStopping = _lifetime.ApplicationStopping;
		ArghServices.ServiceProvider = _services;
		int exitCode;
		try
		{
			exitCode = await _runCliAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Unhandled exception while running the CLI.");
			exitCode = 1;
		}
		finally
		{
			ArghHostRuntime.ApplicationStopping = null;
			ArghServices.ServiceProvider = null;
		}

		try
		{
			Console.Out.Flush();
			Console.Error.Flush();
		}
		catch
		{
		}

		Environment.Exit(exitCode);
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
