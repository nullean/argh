using Xunit;

namespace Nullean.Make.Tests;

/// <summary>Tests that swap <see cref="Console.Out"/> or <see cref="Console.Error"/> join this collection so they never run in parallel.</summary>
[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public class ConsoleCollection;
