namespace Nullean.Make;

/// <summary>Fluent builder surface for targets with a typed per-target argument DTO.</summary>
public interface ITargetBuilder<T> : ITargetBuilder
{
	/// <inheritdoc cref="ITargetBuilder.Named"/>
	new ITargetBuilder<T> Named(string cliName);

	/// <inheritdoc cref="ITargetBuilder.Description"/>
	new ITargetBuilder<T> Description(string text);

	/// <inheritdoc cref="ITargetBuilder.Hidden"/>
	new ITargetBuilder<T> Hidden();

	/// <inheritdoc cref="ITargetBuilder.OnlyWhen"/>
	new ITargetBuilder<T> OnlyWhen(Func<bool> condition);

	/// <inheritdoc cref="ITargetBuilder.DependsOn"/>
	new ITargetBuilder<T> DependsOn(params TargetRef[] refs);

	/// <summary>Executes the given action with a bound instance of <typeparamref name="T"/> parsed from CLI tokens.</summary>
	ITargetBuilder<T> Executes(Action<T> body);

	/// <summary>Executes the given async delegate with a bound instance of <typeparamref name="T"/> parsed from CLI tokens.</summary>
	ITargetBuilder<T> Executes(Func<T, Task> body);
}

/// <summary>
/// Declares a build target whose per-target CLI arguments are bound to an instance of <typeparamref name="T"/>.
/// The record's primary-constructor parameters become CLI flags (PascalCase → kebab-case).
/// </summary>
public delegate ITargetBuilder<T> Target<T>(ITargetBuilder<T> builder);
