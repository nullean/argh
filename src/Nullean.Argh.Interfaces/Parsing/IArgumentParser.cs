namespace Nullean.Argh.Parsing;

/// <summary>Parses a CLI string into a custom type (implemented by the user; invoked from generated code).</summary>
public interface IArgumentParser<T>
{
	bool TryParse(string raw, out T value);
}
