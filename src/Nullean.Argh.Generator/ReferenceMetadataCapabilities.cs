using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Nullean.Argh;

/// <summary>
/// Flags derived from <see cref="MetadataReference.Display"/> suffix checks (ConsoleAppFramework-style), for future conditional emit or diagnostics.
/// </summary>
internal static class ReferenceMetadataCapabilities
{
	internal readonly record struct Capabilities(
		bool HasMicrosoftExtensionsDependencyInjection, bool HasMicrosoftExtensionsHosting
	);

	internal static Capabilities Compute(ImmutableArray<MetadataReference> references)
	{
		var hasDi = false;
		var hasHost = false;
		foreach (var r in references)
		{
			var display = r.Display ?? string.Empty;
			if (display.Length == 0)
				continue;

			if (!hasDi && display.EndsWith("Microsoft.Extensions.DependencyInjection.dll", StringComparison.OrdinalIgnoreCase))
				hasDi = true;

			if (!hasHost && display.EndsWith("Microsoft.Extensions.Hosting.dll", StringComparison.OrdinalIgnoreCase))
				hasHost = true;

			if (hasDi && hasHost)
				break;
		}

		return new Capabilities(hasDi, hasHost);
	}
}
