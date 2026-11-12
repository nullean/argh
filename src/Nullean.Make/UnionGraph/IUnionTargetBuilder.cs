namespace Nullean.Make.UnionGraph;

/// <summary>
/// Fluent builder returned from <see cref="MakeApp{TUnion}.Target()"/> and
/// <see cref="MakeApp{TUnion}.Command()"/> inside the <c>app.Bind(...)</c> lambda.
/// <para>
/// The lambda is called twice per case: once at graph-build time with default values (for
/// metadata), and once at execution time with real CLI-parsed values (for the Executes body).
/// </para>
/// </summary>
public interface IUnionTargetBuilder<TUnion>
{
	/// <summary>Sets the human-readable description shown in help output.</summary>
	IUnionTargetBuilder<TUnion> Description(string text);

	/// <summary>Hides this target from default help output.</summary>
	IUnionTargetBuilder<TUnion> Hidden();

	/// <summary>
	/// Declares dependencies by passing union values.
	/// Implicit union conversion applies: <c>DependsOn(new Clean(), new Build())</c>
	/// when <c>Clean</c>/<c>Build</c> are case types of <typeparamref name="TUnion"/>.
	/// </summary>
	IUnionTargetBuilder<TUnion> DependsOn(params TUnion[] deps);

	/// <summary>Declares a single dependency by case type. The type must be a case of <typeparamref name="TUnion"/>; <c>RunAsync</c> exits with an error otherwise.</summary>
	IUnionTargetBuilder<TUnion> DependsOn<T1>();

	/// <summary>Declares two dependencies by case types.</summary>
	IUnionTargetBuilder<TUnion> DependsOn<T1, T2>();

	/// <summary>Declares three dependencies by case types.</summary>
	IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3>();

	/// <summary>Declares four dependencies by case types.</summary>
	IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3, T4>();

	/// <summary>
	/// Marks this target as a command: it composes other targets that always run (not skippable with <c>-s</c>).
	/// </summary>
	IUnionTargetBuilder<TUnion> Composes(params TUnion[] targets);

	/// <summary>Composes targets by case type.</summary>
	IUnionTargetBuilder<TUnion> Composes<T1>();

	/// <summary>
	/// Gates that run before the composed targets, in order, and that <c>-s</c> skips. The same as
	/// <see cref="DependsOn(TUnion[])"/>, named for commands: <c>app.Command().Requires(new Test()).Composes(new Pack())</c>.
	/// </summary>
	IUnionTargetBuilder<TUnion> Requires(params TUnion[] gates);

	/// <summary>
	/// Composes targets that run side by side, as one stage: each starts once the stages before it have finished, and
	/// the stages after it wait for all of them. Stages run in the order they are declared:
	/// <c>app.Command().Requires(new Test()).Parallel(new PackNuget(), new PackDocker()).Composes(new Publish())</c>.
	/// <c>-j</c> caps how many targets run at once.
	/// </summary>
	IUnionTargetBuilder<TUnion> Parallel(params TUnion[] targets);

	/// <summary>Registers a synchronous execution body. Closed-over case values are the real CLI-parsed ones at execution time.</summary>
	IUnionTargetBuilder<TUnion> Executes(Action body);

	/// <summary>Registers an asynchronous execution body.</summary>
	IUnionTargetBuilder<TUnion> Executes(Func<Task> body);

	/// <summary>
	/// Registers an asynchronous execution body that receives the run's cancellation token. The token is cancelled when
	/// another target fails and the run stops.
	/// </summary>
	IUnionTargetBuilder<TUnion> Executes(Func<CancellationToken, Task> body);
}
