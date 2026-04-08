using Microsoft.Extensions.Hosting;

namespace Nullean.Argh.Hosting;

/// <summary>
/// Host-scoped context for an Argh CLI run: raw arguments and the host shutdown token.
/// </summary>
/// <remarks>
/// <para>
/// The source-generated <c>ArghGenerated.RunAsync</c> currently wires command <see cref="System.Threading.CancellationToken"/>
/// parameters to console cancellation (<see cref="Console.CancelKeyPress"/>). When running under a generic host, this type exposes
/// <see cref="ApplicationStopping"/> so application code can align with <see cref="IHostApplicationLifetime.ApplicationStopping"/>.
/// Unifying generated command cancellation with this token requires a generator change.
/// </para>
/// </remarks>
public sealed class ArghCliHostContext
{
	/// <summary>Creates a new context.</summary>
	public ArghCliHostContext(string[] args, IHostApplicationLifetime hostLifetime)
	{
		Args = args ?? throw new ArgumentNullException(nameof(args));
		if (hostLifetime is null)
			throw new ArgumentNullException(nameof(hostLifetime));
		ApplicationStopping = hostLifetime.ApplicationStopping;
	}

	/// <summary>Command-line arguments passed to <c>AddArgh</c>.</summary>
	public string[] Args { get; }

	/// <summary>Token fired when the host is stopping; use for cooperative shutdown alongside CLI logic.</summary>
	public CancellationToken ApplicationStopping { get; }
}
