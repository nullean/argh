namespace Nullean.Make;

/// <summary>Fluent builder surface for commands (compositions of targets).</summary>
public interface ICommandBuilder : ITargetBuilder
{
	/// <inheritdoc cref="ITargetBuilder.Named"/>
	new ICommandBuilder Named(string cliName);

	/// <inheritdoc cref="ITargetBuilder.Description"/>
	new ICommandBuilder Description(string text);

	/// <inheritdoc cref="ITargetBuilder.Hidden"/>
	new ICommandBuilder Hidden();

	/// <inheritdoc cref="ITargetBuilder.OnlyWhen"/>
	new ICommandBuilder OnlyWhen(Func<bool> condition);

	/// <inheritdoc cref="ITargetBuilder.DependsOn"/>
	new ICommandBuilder DependsOn(params TargetRef[] refs);

	/// <inheritdoc cref="ITargetBuilder.Executes(Action)"/>
	new ICommandBuilder Executes(Action body);

	/// <inheritdoc cref="ITargetBuilder.Executes(Func{Task})"/>
	new ICommandBuilder Executes(Func<Task> body);

	/// <summary>Declares verification-gate targets to run before <c>Composes</c>. Skipped under <c>-s</c>.</summary>
	ICommandBuilder Requires(params TargetRef[] refs);

	/// <summary>Declares the targets that constitute the core work of this command. Always runs.</summary>
	ICommandBuilder Composes(params TargetRef[] refs);

	/// <summary>
	/// Composes targets that run side by side, as one stage: each starts once the stages before it have finished, and
	/// the stages after it wait for all of them. Stages run in the order they are declared. <c>-j</c> caps how many
	/// targets run at once.
	/// </summary>
	ICommandBuilder Parallel(params TargetRef[] refs);
}

/// <summary>
/// Declares a build command — a composition of targets/commands with optional <see cref="ITargetBuilder.Executes"/> trailing body.
/// Use <see cref="ICommandBuilder.Requires"/> for skippable verification gates and <see cref="ICommandBuilder.Composes"/> for the always-run work.
/// </summary>
public delegate ICommandBuilder Command(ICommandBuilder builder);
