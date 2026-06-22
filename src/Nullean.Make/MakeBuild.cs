namespace Nullean.Make;

/// <summary>
/// Base class for build definitions. Declare public properties of type <see cref="Target"/>, <see cref="Target{T}"/>, <see cref="Command"/>, or <see cref="Command{T}"/> to register targets and commands.
/// Annotate <see cref="bool"/> properties with <see cref="FlagAttribute"/> and other properties with <see cref="GlobalOptionAttribute"/> to declare global CLI options.
/// Declare public instance properties of types inheriting <see cref="Namespace"/> to register namespaced target groups.
/// </summary>
public abstract class MakeBuild { }
