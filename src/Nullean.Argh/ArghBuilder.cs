namespace Nullean.Argh;

/// <summary>
/// Default <see cref="IArghBuilder"/> implementation; holds an <see cref="ArghApp"/> for source generator analysis.
/// </summary>
public sealed class ArghBuilder : IArghBuilder
{
	private readonly ArghApp _app;

	public ArghBuilder() : this(new ArghApp())
	{
	}

	internal ArghBuilder(ArghApp app) =>
		_app = app ?? throw new ArgumentNullException(nameof(app));

	internal ArghApp App => _app;

	public IArghBuilder GlobalOptions<T>() where T : class
	{
		_ = _app.GlobalOptions<T>();
		return this;
	}

	public IArghBuilder Add(string name, Delegate handler)
	{
		_ = _app.Add(name, handler);
		return this;
	}

	public IArghBuilder Add<T>() where T : class
	{
		_ = _app.Add<T>();
		return this;
	}

	public IArghBuilder AddNamespace(string name, Action<IArghBuilder> configure)
	{
		_ = _app.AddNamespace(name, configure);
		return this;
	}

	public IArghBuilder CommandNamespaceOptions<T>() where T : class
	{
		_ = _app.CommandNamespaceOptions<T>();
		return this;
	}

	public IArghBuilder UseFilter(Func<CommandContext, CommandFilterDelegate, ValueTask> filter)
	{
		_ = _app.UseFilter(filter);
		return this;
	}

	public IArghBuilder UseFilter<TFilter>() where TFilter : ICommandFilter
	{
		_ = _app.UseFilter<TFilter>();
		return this;
	}

	/// <inheritdoc cref="ArghApp.RunAsync"/>
	public Task<int> RunAsync(string[] args) => ArghRuntime.RunAsync(args);
}

