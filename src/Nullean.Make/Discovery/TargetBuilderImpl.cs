namespace Nullean.Make.Discovery;

internal sealed class TargetBuilderImpl : ITargetBuilder, ICommandBuilder
{
	private readonly TargetNode _node;

	internal TargetBuilderImpl(TargetNode node) => _node = node;

	// ITargetBuilder
	public ITargetBuilder Named(string cliName)
	{
		_node.Route[^1] = cliName;
		return this;
	}
	public ITargetBuilder Description(string text)
	{
		_node.Description = text;
		return this;
	}
	public ITargetBuilder Hidden()
	{
		_node.Hidden = true;
		return this;
	}
	public ITargetBuilder OnlyWhen(Func<bool> condition)
	{
		_node.OnlyWhen = condition;
		return this;
	}

	public ITargetBuilder DependsOn(params TargetRef[] refs)
	{
		foreach (var r in refs)
			_node.AddReference(Execution.StageKind.Requires, r);
		return this;
	}

	public ITargetBuilder After(params TargetRef[] refs)
	{
		_node.After.AddRange(refs);
		return this;
	}

	public ITargetBuilder Cleanup(params TargetRef[] refs)
	{
		_node.Cleanup.AddRange(refs);
		return this;
	}

	public ITargetBuilder NeedsEnv(params string[] variables)
	{
		_node.NeedsEnv.AddRange(variables);
		return this;
	}

	public ITargetBuilder NeedsTool(params string[] tools)
	{
		_node.NeedsTool.AddRange(tools);
		return this;
	}

	public ITargetBuilder Executes(Action body)
	{
		_node.SyncBody = body;
		return this;
	}
	public ITargetBuilder Executes(Func<Task> body)
	{
		_node.AsyncBody = body;
		return this;
	}

	// ICommandBuilder — covariant returns
	ICommandBuilder ICommandBuilder.Named(string cliName)
	{
		_node.Route[^1] = cliName;
		return this;
	}
	ICommandBuilder ICommandBuilder.Description(string text)
	{
		_node.Description = text;
		return this;
	}
	ICommandBuilder ICommandBuilder.Hidden()
	{
		_node.Hidden = true;
		return this;
	}
	ICommandBuilder ICommandBuilder.OnlyWhen(Func<bool> c)
	{
		_node.OnlyWhen = c;
		return this;
	}

	ICommandBuilder ICommandBuilder.DependsOn(params TargetRef[] refs)
	{
		foreach (var r in refs)
			_node.AddReference(Execution.StageKind.Requires, r);
		return this;
	}

	ICommandBuilder ICommandBuilder.After(params TargetRef[] refs)
	{
		_node.After.AddRange(refs);
		return this;
	}

	ICommandBuilder ICommandBuilder.Cleanup(params TargetRef[] refs)
	{
		_node.Cleanup.AddRange(refs);
		return this;
	}

	ICommandBuilder ICommandBuilder.NeedsEnv(params string[] variables)
	{
		_node.NeedsEnv.AddRange(variables);
		return this;
	}

	ICommandBuilder ICommandBuilder.NeedsTool(params string[] tools)
	{
		_node.NeedsTool.AddRange(tools);
		return this;
	}

	ICommandBuilder ICommandBuilder.Executes(Action body)
	{
		_node.SyncBody = body;
		return this;
	}
	ICommandBuilder ICommandBuilder.Executes(Func<Task> body)
	{
		_node.AsyncBody = body;
		return this;
	}

	ICommandBuilder ICommandBuilder.Requires(params TargetRef[] refs)
	{
		foreach (var r in refs)
			_node.AddReference(Execution.StageKind.Requires, r);
		return this;
	}

	ICommandBuilder ICommandBuilder.Parallel(params TargetRef[] refs)
	{
		_node.AddParallel(refs);
		return this;
	}

	ICommandBuilder ICommandBuilder.Composes(params TargetRef[] refs)
	{
		foreach (var r in refs)
			_node.AddReference(Execution.StageKind.Composes, r);
		return this;
	}
}

internal sealed class TargetBuilderImplOfT<T> : ITargetBuilder<T>, ICommandBuilder<T>
{
	private readonly TargetNode _node;

	internal TargetBuilderImplOfT(TargetNode node)
	{
		_node = node;
		_node.DtoType = typeof(T);
	}

	// ITargetBuilder<T>
	public ITargetBuilder<T> Named(string cliName)
	{
		_node.Route[^1] = cliName;
		return this;
	}
	public ITargetBuilder<T> Description(string text)
	{
		_node.Description = text;
		return this;
	}
	public ITargetBuilder<T> Hidden()
	{
		_node.Hidden = true;
		return this;
	}
	public ITargetBuilder<T> OnlyWhen(Func<bool> condition)
	{
		_node.OnlyWhen = condition;
		return this;
	}
	public ITargetBuilder<T> DependsOn(params TargetRef[] refs)
	{
		foreach (var r in refs)
			_node.AddReference(Execution.StageKind.Requires, r);
		return this;
	}
	public ITargetBuilder<T> After(params TargetRef[] refs)
	{
		_node.After.AddRange(refs);
		return this;
	}

	public ITargetBuilder<T> Cleanup(params TargetRef[] refs)
	{
		_node.Cleanup.AddRange(refs);
		return this;
	}

	public ITargetBuilder<T> NeedsEnv(params string[] variables)
	{
		_node.NeedsEnv.AddRange(variables);
		return this;
	}

	public ITargetBuilder<T> NeedsTool(params string[] tools)
	{
		_node.NeedsTool.AddRange(tools);
		return this;
	}

	public ITargetBuilder<T> Executes(Action<T> body)
	{
		_node.TypedBody = body;
		return this;
	}
	public ITargetBuilder<T> Executes(Func<T, Task> body)
	{
		_node.TypedBody = body;
		return this;
	}

	// ITargetBuilder (untyped) — explicit for interface satisfaction
	ITargetBuilder ITargetBuilder.Named(string n) => Named(n);
	ITargetBuilder ITargetBuilder.Description(string d) => Description(d);
	ITargetBuilder ITargetBuilder.Hidden() => Hidden();
	ITargetBuilder ITargetBuilder.OnlyWhen(Func<bool> c) => OnlyWhen(c);
	ITargetBuilder ITargetBuilder.DependsOn(params TargetRef[] refs) => DependsOn(refs);
	ITargetBuilder ITargetBuilder.After(params TargetRef[] refs) => After(refs);
	ITargetBuilder ITargetBuilder.Cleanup(params TargetRef[] refs) => Cleanup(refs);
	ITargetBuilder ITargetBuilder.NeedsEnv(params string[] variables) => NeedsEnv(variables);
	ITargetBuilder ITargetBuilder.NeedsTool(params string[] tools) => NeedsTool(tools);
	ITargetBuilder ITargetBuilder.Executes(Action body)
	{
		_node.SyncBody = body;
		return this;
	}
	ITargetBuilder ITargetBuilder.Executes(Func<Task> body)
	{
		_node.AsyncBody = body;
		return this;
	}

	// ICommandBuilder<T>
	ICommandBuilder<T> ICommandBuilder<T>.Requires(params TargetRef[] refs)
	{
		foreach (var r in refs)
			_node.AddReference(Execution.StageKind.Requires, r);
		return this;
	}
	ICommandBuilder<T> ICommandBuilder<T>.Parallel(params TargetRef[] refs)
	{
		_node.AddParallel(refs);
		return this;
	}

	ICommandBuilder<T> ICommandBuilder<T>.Composes(params TargetRef[] refs)
	{
		foreach (var r in refs)
			_node.AddReference(Execution.StageKind.Composes, r);
		return this;
	}
}
