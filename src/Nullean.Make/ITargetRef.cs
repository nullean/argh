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

	/// <summary>The value the target runs with, or <c>null</c> for its default value.</summary>
	internal object? Value { get; }

	internal TargetRef(MethodInfo m, object? value = null)
	{
		Method = m;
		Value = value;
	}

	/// <summary>Accepts any target or command delegate. Identity is the underlying <see cref="MethodInfo"/>.</summary>
	public static implicit operator TargetRef(Delegate d) => new(d.Method);
}

/// <summary>Names a target with typed arguments together with the value it runs with.</summary>
public static class TargetRefExtensions
{
	/// <summary>
	/// A dependency on <paramref name="target"/> that runs it with <paramref name="value"/>:
	/// <c>.DependsOn(Test.With(new("Unit")))</c>. Two references with equal values run the target once; with different
	/// values, once each.
	/// </summary>
	public static TargetRef With<T>(this Target<T> target, T value) => new(target.Method, value);

	/// <inheritdoc cref="With{T}(Target{T}, T)"/>
	public static TargetRef With<T>(this Command<T> command, T value) => new(command.Method, value);
}
