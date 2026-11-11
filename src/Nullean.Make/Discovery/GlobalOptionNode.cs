using System.Reflection;

namespace Nullean.Make.Discovery;

internal sealed class GlobalOptionNode
{
	public PropertyInfo? Property { get; init; }
	public required string Long { get; init; }
	public string? Short { get; init; }
	public string? Description { get; init; }
	public bool IsFlag { get; init; }
	/// <summary>The option's value type when no <see cref="Property"/> backs it, as for <c>MakeApp.Option&lt;T&gt;</c>.</summary>
	public Type? ValueType { get; init; }
}
