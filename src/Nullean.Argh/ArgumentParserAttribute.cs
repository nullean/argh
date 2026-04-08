namespace Nullean.Argh;

/// <summary>Specifies an <see cref="IArgumentParser{T}"/> implementation used to parse this parameter.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ArgumentParserAttribute : Attribute
{
	public ArgumentParserAttribute(Type parserType) => ParserType = parserType;

	public Type ParserType { get; }
}
