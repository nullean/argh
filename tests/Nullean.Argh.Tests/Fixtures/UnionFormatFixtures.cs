using Nullean.Argh;

namespace Nullean.Argh.Tests.Fixtures;

public record class Json(bool Pretty = false, int Indent = 2);
public record class Table(bool Pretty = false);
public record class Csv;

public union Format(Table, Json, Csv);

internal static class UnionFormatHandlers
{
	/// <summary>Output in a specific format (flag mode: --format &lt;case&gt;).</summary>
	internal static void FormatFlag(TestGlobalCliOptions g, Format format)
	{
		var result = format.Value switch
		{
			Json j  => $"json:pretty={j.Pretty}:indent={j.Indent}",
			Table t => $"table:pretty={t.Pretty}",
			Csv     => "csv",
			_       => "unknown"
		};
		Console.WriteLine(result);
	}

	/// <summary>Output in a specific format (argument mode: &lt;format-case&gt; [--pretty] ...).</summary>
	internal static void FormatArg(TestGlobalCliOptions g, [Argument] Format format)
	{
		var result = format.Value switch
		{
			Json j  => $"json:pretty={j.Pretty}:indent={j.Indent}",
			Table t => $"table:pretty={t.Pretty}",
			Csv     => "csv",
			_       => "unknown"
		};
		Console.WriteLine(result);
	}
}
