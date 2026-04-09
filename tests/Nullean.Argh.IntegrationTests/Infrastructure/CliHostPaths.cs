namespace Nullean.Argh.IntegrationTests.Infrastructure;

/// <summary>Paths and names for the test CLI host binary (see <see cref="CliHostRunner"/>).</summary>
internal static class CliHostPaths
{
	internal const string CliHostDllFileName = "Nullean.Argh.Tests.CliHost.dll";

	/// <summary>Assembly simple name embedded in generated help <c>Usage:</c> lines.</summary>
	internal const string CliHostAssemblyName = "Nullean.Argh.Tests.CliHost";
}
