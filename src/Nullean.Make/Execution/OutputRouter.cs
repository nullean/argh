using System.Text;

namespace Nullean.Make.Execution;

/// <summary>How the output of targets that run at the same time reaches the console.</summary>
internal enum OutputMode
{
	/// <summary>Each line as it is written, with the target's name in front of it.</summary>
	Prefixed,

	/// <summary>A target's lines held back and written together when it finishes.</summary>
	Grouped
}

/// <summary>
/// Stands in for <see cref="Console.Out"/> and <see cref="Console.Error"/> while targets run side by side. A target's
/// task sets <see cref="Current"/>; what it writes goes through the target's sink, and everything else goes straight
/// through. A line is written whole, so lines of two targets never mix.
/// </summary>
internal sealed class OutputRouter : TextWriter
{
	private static readonly AsyncLocal<TargetSink?> _current = new();

	private readonly TextWriter _inner;
	private readonly bool _error;

	public OutputRouter(TextWriter inner, bool error)
	{
		_inner = inner;
		_error = error;
	}

	/// <summary>The sink of the target whose task is running, or <c>null</c> outside one.</summary>
	public static TargetSink? Current { get => _current.Value; set => _current.Value = value; }

	public TextWriter Inner => _inner;

	public override Encoding Encoding => _inner.Encoding;

	public override void Write(char value)
	{
		if (Current is { } sink)
			sink.Write(value, _error);
		else
			_inner.Write(value);
	}

	public override void Write(string? value)
	{
		if (Current is { } sink)
			sink.Write(value, _error);
		else
			_inner.Write(value);
	}

	public override void WriteLine(string? value)
	{
		if (Current is { } sink)
		{
			sink.Write(value, _error);
			sink.Write('\n', _error);
		}
		else
			_inner.WriteLine(value);
	}

	public override void Flush() => _inner.Flush();
}

/// <summary>Collects one target's output into lines, and writes them prefixed or keeps them for the end.</summary>
internal sealed class TargetSink(string name, OutputMode mode, TextWriter stdout, TextWriter stderr, object gate)
{
	private readonly StringBuilder _out = new();
	private readonly StringBuilder _err = new();
	private readonly List<(bool Error, string Line)> _held = new();

	public void Write(char value, bool error)
	{
		var buffer = error ? _err : _out;
		if (value == '\n')
		{
			Emit(error, buffer.ToString().TrimEnd('\r'));
			buffer.Clear();
		}
		else
			buffer.Append(value);
	}

	public void Write(string? value, bool error)
	{
		if (value is null)
			return;
		foreach (var c in value)
			Write(c, error);
	}

	/// <summary>A line written by the target's process, which arrives whole.</summary>
	public void Line(string line, bool error) => Emit(error, line);

	private void Emit(bool error, string line)
	{
		if (mode == OutputMode.Grouped)
		{
			lock (_held)
				_held.Add((error, line));
			return;
		}
		lock (gate)
			(error ? stderr : stdout).WriteLine($"{CliPrefix(name)}{line}");
	}

	/// <summary>Writes what is left in the buffers and, when grouped, every line held back.</summary>
	public void Complete()
	{
		if (_out.Length > 0)
			Write('\n', false);
		if (_err.Length > 0)
			Write('\n', true);
		if (mode != OutputMode.Grouped)
			return;

		lock (gate)
		{
			var actions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
			if (actions)
				stdout.WriteLine($"::group::{name}");
			lock (_held)
			{
				foreach (var (isError, line) in _held)
					(isError ? stderr : stdout).WriteLine(line);
			}
			if (actions)
				stdout.WriteLine("::endgroup::");
		}
	}

	private static string CliPrefix(string name) => Nullean.Argh.Help.CliHelpFormatting.DocRemarksLine($"[{name}] ");
}
