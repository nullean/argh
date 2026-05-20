using System.Reflection;

namespace Nullean.Make.Discovery;

internal sealed class GlobalOptionNode
{
	public PropertyInfo? Property { get; init; }
	public required string Long { get; init; }
	public string? Short { get; init; }
	public string? Description { get; init; }
	public bool IsFlag { get; init; }
}
