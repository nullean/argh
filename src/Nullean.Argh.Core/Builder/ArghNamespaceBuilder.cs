using System.Diagnostics.CodeAnalysis;
using Nullean.Argh.Middleware;
using Nullean.Argh.Runtime;

namespace Nullean.Argh.Builder;

/// <summary>
/// <see cref="IArghNamespaceBuilder"/> implementation wrapping an <see cref="ArghBuilder"/> for the same child <see cref="ArghApp"/>.
/// </summary>
public sealed class ArghNamespaceBuilder : IArghNamespaceBuilder
{
	private readonly ArghBuilder _inner;

	internal ArghNamespaceBuilder(ArghApp app, string segment)
	{
		_inner = new ArghBuilder(app);
		Segment = segment;
	}

	/// <inheritdoc />
	public string Segment { get; }

	/// <inheritdoc />
	public IArghNamespaceBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _inner.UseGlobalOptions<T>();
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder Map(string name, Delegate handler)
	{
		_ = _inner.Map(name, handler);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _inner.Map<T>();
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapRoot(Delegate handler)
	{
		_ = _inner.MapRoot(handler);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = _inner.MapNamespace(name, description, configure);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name) where TNs : class
	{
		_ = _inner.App.MapNamespace<TNs>(name);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghBuilder> configure) where TNs : class
	{
		_ = _inner.MapNamespace<TNs>(name, configure);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _inner.App.MapNamespace<TNs>(name, configure);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _inner.App.MapNamespace<TNs>(configure);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _inner.UseNamespaceOptions<T>();
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		_ = _inner.UseMiddleware(middleware);
		return this;
	}

	/// <inheritdoc />
	public IArghNamespaceBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() where TMiddleware : ICommandMiddleware
	{
		_ = _inner.UseMiddleware<TMiddleware>();
		return this;
	}

	/// <inheritdoc />
	public Task<int> RunAsync(string[] args) => _inner.RunAsync(args);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		UseGlobalOptions<T>();

	/// <inheritdoc />
	IArghBuilder IArghBuilder.Map(string name, Delegate handler) => Map(name, handler);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class => Map<T>();

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapRoot(Delegate handler) => MapRoot(handler);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace(string name, string description, Action<IArghBuilder> configure) =>
		MapNamespace(name, description, configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name) where TNs : class =>
		MapNamespace<TNs>(name);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(name, configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(name, configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(configure);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		UseNamespaceOptions<T>();

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware) =>
		UseMiddleware(middleware);

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() =>
		UseMiddleware<TMiddleware>();

	/// <inheritdoc />
	Task<int> IArghBuilder.RunAsync(string[] args) => RunAsync(args);
}
