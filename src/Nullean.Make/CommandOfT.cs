namespace Nullean.Make;

/// <summary>Fluent builder surface for commands with a typed per-command argument DTO.</summary>
public interface ICommandBuilder<T> : ITargetBuilder<T>
{
	/// <inheritdoc cref="ICommandBuilder.Requires"/>
	ICommandBuilder<T> Requires(params TargetRef[] refs);

	/// <inheritdoc cref="ICommandBuilder.Composes"/>
	ICommandBuilder<T> Composes(params TargetRef[] refs);
}

/// <summary>
/// Declares a build command whose trailing body receives a typed argument DTO of type <typeparamref name="T"/>.
/// </summary>
public delegate ICommandBuilder<T> Command<T>(ICommandBuilder<T> builder);
