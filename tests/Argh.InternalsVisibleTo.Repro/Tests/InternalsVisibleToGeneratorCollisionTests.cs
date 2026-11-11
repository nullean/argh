using AwesomeAssertions;

namespace Argh.InternalsVisibleTo.Repro.Tests;

/// <summary>Ensures the test project (generator + InternalsVisibleTo to CLI app) compiles without CS0436.</summary>
public class InternalsVisibleToGeneratorCollisionTests
{
	[Test]
	public void Placeholder() => true.Should().BeTrue();
}
