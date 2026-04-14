namespace Hosted;

/// <summary>Named handlers registered with <c>Add</c> inside <c>AddArgh</c> for XML-capable &quot;lambda-style&quot; commands.</summary>
internal static class HostedLocalHandlers
{
	/// <summary>Echo a token; documented for generated help.</summary>
	/// <param name="token">-t,--token, Token to print.</param>
	internal static void DocEcho(HostedGlobalCliOptions g, string token) =>
		Console.WriteLine($"hosted:doc-echo:{token}");
}
