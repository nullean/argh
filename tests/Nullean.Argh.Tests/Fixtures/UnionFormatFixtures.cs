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

public enum Fill
{
	Solid,
	[EnumValue("dash")] Dashed,
	None = 5
}

public record class Circle(float Radius = 1.5f, Fill Fill = Fill.None);
public record class Rect(decimal Width = 2.5m, bool Rounded = true);

public union Shape(Circle, Rect);

internal static class UnionShapeHandlers
{
	/// <summary>Draw a shape (union case properties of enum, float, decimal and default-true bool types).</summary>
	internal static void Draw(TestGlobalCliOptions g, Shape shape)
	{
		var inv = System.Globalization.CultureInfo.InvariantCulture;
		var result = shape.Value switch
		{
			Circle c => string.Create(inv, $"circle:radius={c.Radius}:fill={c.Fill}"),
			Rect r   => string.Create(inv, $"rect:width={r.Width}:rounded={r.Rounded}"),
			_        => "unknown"
		};
		Console.WriteLine(result);
	}
}
