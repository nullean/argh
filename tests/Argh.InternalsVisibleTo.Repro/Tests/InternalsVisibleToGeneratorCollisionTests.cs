using Xunit;

namespace Argh.InternalsVisibleTo.Repro.Tests;

/// <summary>Ensures the test project (generator + InternalsVisibleTo to CLI app) compiles without CS0436.</summary>
public class InternalsVisibleToGeneratorCollisionTests
{
	[Fact]
	public void Placeholder() => Assert.True(true);
}
