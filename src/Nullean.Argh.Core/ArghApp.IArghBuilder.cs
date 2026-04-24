using System.Diagnostics.CodeAnalysis;
using Nullean.Argh.Builder;
using Nullean.Argh.Middleware;

namespace Nullean.Argh;

public sealed partial class ArghApp
{
	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseGlobalOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = UseGlobalOptions<T>();
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.Map(string name, Delegate handler)
	{
		_ = Map(name, handler);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.Map<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = Map<T>();
		return this;
	}

	/// <inheritdoc />
	IArghRootBuilder IArghRootBuilder.UseCliDescription(string description)
	{
		_ = UseCliDescription(description);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapRoot(Delegate handler)
	{
		_ = MapRoot(handler);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace(string name, string description, Action<IArghBuilder> configure)
	{
		_ = MapNamespace(name, description, configure);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name) where T : class
	{
		_ = MapNamespace<T>(name);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghBuilder> configure) where T : class
	{
		_ = MapNamespace<T>(name, configure);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, Action<IArghNamespaceBuilder> configure) where T : class
	{
		_ = MapNamespace<T>(name, configure);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapNamespace<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(Action<IArghNamespaceBuilder> configure) where T : class
	{
		_ = MapNamespace<T>(configure);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.MapAndRootAlias<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = MapAndRootAlias<T>();
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseNamespaceOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class
	{
		_ = UseNamespaceOptions<T>();
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseMiddleware(Func<CommandContext, CommandMiddlewareDelegate, ValueTask> middleware)
	{
		_ = UseMiddleware(middleware);
		return this;
	}

	/// <inheritdoc />
	IArghBuilder IArghBuilder.UseMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
	{
		_ = UseMiddleware<TMiddleware>();
		return this;
	}

	/// <inheritdoc />
	Task<int> IArghBuilder.RunAsync(string[] args) => RunAsync(args);
}
