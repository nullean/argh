using System.Threading;
using Nullean.Make.Execution;

namespace Nullean.Make;

/// <summary>Ambient context available to target bodies. Access via <see cref="Current"/> from within <c>.Executes(...)</c> callbacks.</summary>
public sealed class MakeContext
{
	private static readonly AsyncLocal<MakeContext?> _current = new();

	/// <summary>The context for the currently-executing target. Non-null only inside an <c>.Executes(...)</c> callback.</summary>
	public static MakeContext? Current { get => _current.Value; internal set => _current.Value = value; }

	private readonly Dictionary<string, string> _rawValues;
	private readonly bool _singleTarget;

	internal MakeContext(Dictionary<string, string> rawValues, bool singleTarget)
	{
		_rawValues = rawValues;
		_singleTarget = singleTarget;
	}

	/// <summary>Returns <see langword="true"/> when the <c>-s</c> / <c>--single-target</c> flag was passed.</summary>
	public bool IsSingleTarget => _singleTarget;

	/// <summary>The raw CLI tokens that followed the route (available to F# payload-case SyncBody closures).</summary>
	public string[] TargetArgs { get; internal set; } = [];

	/// <summary>
	/// Cancelled when another target of the run fails and the run stops. A body that runs for long should pass it on;
	/// <see cref="Shell.Exec(string)"/> already does.
	/// </summary>
	public CancellationToken CancellationToken { get; private init; }

	/// <summary>Where the target's output goes while targets run side by side, or <c>null</c> when it has the console.</summary>
	internal TargetSink? Sink { get; private init; }

	/// <summary>Reads the raw string value for a global option. Returns <see langword="null"/> when not supplied.</summary>
	public string? GetRaw(string longName) => _rawValues.TryGetValue(longName.TrimStart('-'), out var v) ? v : null;

	/// <summary>The context one target runs in.</summary>
	internal MakeContext ForTarget(CancellationToken token, TargetSink? sink) =>
		new(_rawValues, _singleTarget) { TargetArgs = TargetArgs, CancellationToken = token, Sink = sink };
}
