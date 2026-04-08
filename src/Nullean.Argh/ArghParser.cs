namespace Nullean.Argh;

/// <summary>
/// High-level parse/bind entry points. Full binding to user DTOs is provided by source-generated code; these members surface the API until the generator emits concrete implementations.
/// </summary>
public static class ArghParser
{
	/// <summary>
	/// Parses CLI arguments and binds them to <typeparamref name="T"/>. Not supported at runtime until the generator supplies bindings.
	/// </summary>
	/// <typeparam name="T">CLI options or command DTO type.</typeparam>
	/// <param name="args">Full command line as a single string (shell-style splitting is not performed here).</param>
	/// <exception cref="NotSupportedException">Always thrown until source-generated bindings exist.</exception>
	public static T Bind<T>(string args)
	{
		if (args is null)
			throw new ArgumentNullException(nameof(args));
		throw new NotSupportedException(
			"CLI binding for '" + typeof(T).FullName + "' is not available until the source generator emits a parser for this type. Use the generated registration and entry point for your application.");
	}

	/// <summary>
	/// Parses CLI arguments from a character span and binds them to <typeparamref name="T"/>. Not supported at runtime until the generator supplies bindings.
	/// </summary>
	/// <typeparam name="T">CLI options or command DTO type.</typeparam>
	/// <param name="args">Raw command-line text.</param>
	/// <exception cref="NotSupportedException">Always thrown until source-generated bindings exist.</exception>
	public static T Bind<T>(ReadOnlySpan<char> args)
	{
		throw new NotSupportedException(
			"CLI binding for '" + typeof(T).FullName + "' is not available until the source generator emits a parser for this type. Use the generated registration and entry point for your application.");
	}
}
