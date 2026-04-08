using Microsoft.Extensions.Hosting;

namespace Nullean.Argh.Hosting;

/// <summary>
/// Host-scoped context for an Argh CLI run: raw arguments and the host shutdown token.
/// </summary>
/// <remarks>
/// <para>
/// Command <see cref="System.Threading.CancellationToken"/> parameters in generated code use a token linked from
/// <see cref="Console.CancelKeyPress"/> and <see cref="Nullean.Argh.ArghHostRuntime.ApplicationStopping"/> (set for hosted runs).
/// This property exposes the same host shutdown token for application code outside the generated parser.
/// </para>
/// </remarks>
public sealed class ArghHostingContext
{
	/// <summary>Creates a new context.</summary>
	public ArghHostingContext(string[] args, IHostApplicationLifetime hostLifetime)
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
