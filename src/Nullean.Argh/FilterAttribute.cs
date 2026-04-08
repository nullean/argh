namespace Nullean.Argh;

/// <summary>
/// Associates a per-command filter with a command method. The source generator reads this metadata; it has no runtime behavior until the generator emits the filter pipeline.
/// </summary>
/// <typeparam name="TFilter">A type implementing <see cref="ICommandFilter"/>.</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class FilterAttribute<TFilter> : Attribute where TFilter : ICommandFilter;
