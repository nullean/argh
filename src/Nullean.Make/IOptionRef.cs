namespace Nullean.Make;

/// <summary>Handle to a global option declared on a <see cref="MakeBuild"/> subclass. Obtain instances from <see cref="MakeContext"/> to read values at execution time.</summary>
public interface IOptionRef<out T>
{
	internal string Long { get; }
	internal string? Short { get; }
	internal string? Description { get; }
	internal T DefaultValue { get; }
}
