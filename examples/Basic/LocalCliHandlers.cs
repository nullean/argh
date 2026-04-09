namespace Basic;

/// <summary>Handlers that live outside <c>CommandHandlers</c> to show XML docs on &quot;lambda-style&quot; registrations.</summary>
internal static class LocalCliHandlers
{
	/// <summary>Echo a line; registered with <c>Add("doc-echo", LocalCliHandlers.DocEcho)</c>.</summary>
	/// <param name="line">-l,--line, Text to echo.</param>
	/// <example>doc-echo --line "example"</example>
	internal static void DocEcho(string line) =>
		Console.WriteLine($"basic:doc-echo:{line}");
}
