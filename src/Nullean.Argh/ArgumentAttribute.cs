namespace Nullean.Argh;

/// <summary>Marks a parameter as a positional CLI argument (successive positions starting at 0).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ArgumentAttribute : Attribute;
