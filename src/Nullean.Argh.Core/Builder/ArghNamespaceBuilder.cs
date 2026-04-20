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

	public IArghNamespaceBuilder UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _inner.UseGlobalOptions<T>();
		return this;
	}

	public IArghNamespaceBuilder Map(string name, Delegate handler)
	{
		_ = _inner.Map(name, handler);
		return this;
	}

	public IArghNamespaceBuilder Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _inner.Map<T>();
		return this;
	}

	public IArghNamespaceBuilder MapRoot(Delegate handler)
	{
		_ = _inner.MapRoot(handler);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = _inner.MapNamespace(name, description, configure);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name) where TNs : class
	{
		_ = _inner.App.MapNamespace<TNs>(name);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghBuilder> configure) where TNs : class
	{
		_ = _inner.MapNamespace<TNs>(name, configure);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _inner.App.MapNamespace<TNs>(name, configure);
		return this;
	}

	public IArghNamespaceBuilder MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class
	{
		_ = _inner.App.MapNamespace<TNs>(configure);
		return this;
	}

	public IArghNamespaceBuilder UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = _inner.UseNamespaceOptions<T>();
		return this;
	}

	public IArghNamespaceBuilder UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		_ = _inner.UseMiddleware(middleware);
		return this;
	}

	public IArghNamespaceBuilder UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() where TMiddleware : ICommandMiddleware
	{
		_ = _inner.UseMiddleware<TMiddleware>();
		return this;
	}

	public Task<int> RunAsync(string[] args) => _inner.RunAsync(args);

	IArghBuilder IArghBuilder.UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		UseGlobalOptions<T>();

	IArghBuilder IArghBuilder.Map(string name, Delegate handler) => Map(name, handler);

	IArghBuilder IArghBuilder.Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class => Map<T>();

	IArghBuilder IArghBuilder.MapRoot(Delegate handler) => MapRoot(handler);

	IArghBuilder IArghBuilder.MapNamespace(string name, string description, Action<IArghBuilder> configure) =>
		MapNamespace(name, description, configure);

	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name) where TNs : class =>
		MapNamespace<TNs>(name);

	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(name, configure);

	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(string name, Action<IArghNamespaceBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(name, configure);

	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNs>(Action<IArghNamespaceBuilder> configure) where TNs : class =>
		MapNamespace<TNs>(configure);

	IArghBuilder IArghBuilder.UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class =>
		UseNamespaceOptions<T>();

	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware) =>
		UseMiddleware(middleware);

	IArghBuilder IArghBuilder.UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>() =>
		UseMiddleware<TMiddleware>();

	Task<int> IArghBuilder.RunAsync(string[] args) => RunAsync(args);
}
