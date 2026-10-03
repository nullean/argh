using System.Collections.Concurrent;

namespace Nullean.Argh.Validation;

/// <summary>One validation failure for a bound option object.</summary>
public readonly struct ArghValidationError
{
	/// <param name="message">What is wrong, for example <c>must be between 1 and 65535</c>.</param>
	/// <param name="member">The CLR member the error is about, or null when it concerns the object as a whole.</param>
	public ArghValidationError(string message, string? member = null)
	{
		Message = message ?? throw new ArgumentNullException(nameof(message));
		Member = member;
	}

	public string Message { get; }

	public string? Member { get; }
}

/// <summary>
/// A registry of validators for bound option objects: <c>[AsParameters]</c> types and global/namespace options types. The generated parser runs the registered validator for each bound object
/// right after its built-in checks (<c>[Range]</c>, <c>[Existing]</c>, and so on), so a failure prints exactly like one of those and exits with code 2.
/// </summary>
/// <remarks>
/// Validators are looked up by the object's runtime type, then its base types, so a validator for a base options type covers derived ones. Nothing is registered by default, and the
/// generated code skips all of this when the registry is empty. Register at startup, before <c>RunAsync</c>.
/// </remarks>
public static class ArghValidation
{
	private static readonly ConcurrentDictionary<Type, Func<object, IEnumerable<ArghValidationError>>> Validators = new();

	/// <summary>True once any validator is registered. The generated code checks this first so an app with none pays nothing.</summary>
	public static bool HasValidators => !Validators.IsEmpty;

	/// <summary>Registers (or replaces) the validator for <typeparamref name="T"/>.</summary>
	public static void Register<T>(Func<T, IEnumerable<ArghValidationError>> validate) where T : class
	{
		if (validate is null)
			throw new ArgumentNullException(nameof(validate));
		Validators[typeof(T)] = o => validate((T)o);
	}

	/// <summary>Removes every registered validator.</summary>
	public static void Clear() => Validators.Clear();

	/// <summary>
	/// Runs the validator registered for <paramref name="value"/>'s type, if any, and writes <c>Error: --flag: message</c> to stderr for each failure.
	/// Returns true when there is nothing to report. <paramref name="firstFlag"/> is the flag of the first failure, for the help rows the generated code prints after.
	/// </summary>
	/// <param name="value">The bound object.</param>
	/// <param name="memberFlags">CLR member name to flag name (without dashes), as the generator knows it. Members not in it are reported without a flag.</param>
	/// <param name="firstFlag">The flag of the first failure that has one.</param>
	public static bool Validate(object? value, IReadOnlyDictionary<string, string>? memberFlags, out string? firstFlag)
	{
		firstFlag = null;
		if (value is null)
			return true;
		for (var type = value.GetType(); type is not null; type = type.BaseType)
		{
			if (!Validators.TryGetValue(type, out var validate))
				continue;
			var ok = true;
			foreach (var error in validate(value))
			{
				ok = false;
				string? flag = null;
				if (error.Member is not null && memberFlags is not null)
					memberFlags.TryGetValue(error.Member, out flag);
				Console.Error.WriteLine(flag is null ? $"Error: {error.Message}" : $"Error: --{flag}: {error.Message}");
				firstFlag ??= flag;
			}
			return ok;
		}
		return true;
	}
}
