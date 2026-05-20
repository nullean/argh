using System.Reflection;

namespace Nullean.Make;

/// <summary>
/// Lightweight handle used for typed cross-references in <c>.DependsOn()</c>, <c>.Requires()</c>, and <c>.Composes()</c>.
/// Carries the stable <see cref="MethodInfo"/> of the configure delegate so the dep graph can be resolved by identity.
/// Implicit conversions exist from all target/command delegate types.
/// </summary>
public readonly struct TargetRef
{
	internal MethodInfo Method { get; }

	internal TargetRef(MethodInfo m) => Method = m;

	/// <summary>Accepts any target or command delegate. Identity is the underlying <see cref="MethodInfo"/>.</summary>
	public static implicit operator TargetRef(Delegate d) => new(d.Method);
}
