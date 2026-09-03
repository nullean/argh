namespace Nullean.Make.UnionGraph;

/// <summary>
/// Fluent builder returned from <see cref="UnionMakeApp{TUnion}.Target()"/> and
/// <see cref="UnionMakeApp{TUnion}.Command()"/> inside the <c>app.Bind(...)</c> lambda.
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

	/// <summary>Declares a single dependency by case type. The case type must have a parameterless (or all-default) constructor.</summary>
	IUnionTargetBuilder<TUnion> DependsOn<T1>() where T1 : new();

	/// <summary>Declares two dependencies by case types.</summary>
	IUnionTargetBuilder<TUnion> DependsOn<T1, T2>() where T1 : new() where T2 : new();

	/// <summary>Declares three dependencies by case types.</summary>
	IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3>()
		where T1 : new() where T2 : new() where T3 : new();

	/// <summary>Declares four dependencies by case types.</summary>
	IUnionTargetBuilder<TUnion> DependsOn<T1, T2, T3, T4>()
		where T1 : new() where T2 : new() where T3 : new() where T4 : new();

	/// <summary>
	/// Marks this target as a command: it composes other targets that always run (not skippable with <c>-s</c>).
	/// </summary>
	IUnionTargetBuilder<TUnion> Composes(params TUnion[] targets);

	/// <summary>Composes targets by case type.</summary>
	IUnionTargetBuilder<TUnion> Composes<T1>() where T1 : new();

	/// <summary>Registers a synchronous execution body. Closed-over case values are the real CLI-parsed ones at execution time.</summary>
	IUnionTargetBuilder<TUnion> Executes(Action body);

	/// <summary>Registers an asynchronous execution body.</summary>
	IUnionTargetBuilder<TUnion> Executes(Func<Task> body);
}
