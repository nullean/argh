using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nullean.Argh.Hosting;

/// <summary>
/// Runs the generated CLI entry (<c>ArghGenerated.RunAsync</c>) after the host has started, then stops the host when the run completes.
/// </summary>
internal sealed class ArghCliHostedService : IHostedService
{
	private readonly Func<Task<int>> _runCliAsync;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly ILogger<ArghCliHostedService>? _logger;
	private Task? _runTask;

	public ArghCliHostedService(
		Func<Task<int>> runCliAsync,
		IHostApplicationLifetime lifetime,
		ILogger<ArghCliHostedService>? logger = null)
	{
		_runCliAsync = runCliAsync ?? throw new ArgumentNullException(nameof(runCliAsync));
		_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
		_logger = logger;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		// Run after the host has completed startup so logging and other services are ready; avoids StopApplication racing "started".
		_lifetime.ApplicationStarted.Register(() => { _runTask = RunCliAndStopHostAsync(); });
		return Task.CompletedTask;
	}

	private async Task RunCliAndStopHostAsync()
	{
		try
		{
			var exitCode = await _runCliAsync().ConfigureAwait(false);
			Environment.ExitCode = exitCode;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Unhandled exception while running the CLI.");
			Environment.ExitCode = 1;
		}
		finally
		{
			_lifetime.StopApplication();
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		var t = _runTask;
		if (t is not null)
			await t.ConfigureAwait(false);
	}
}
