using Xunit;

namespace Nullean.Argh.Tests;

/// <summary>
/// Serializes tests that replace <see cref="Console.Out"/> / <see cref="Console.Error"/> — the console is process-global.
/// </summary>
[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleTestCollection;
