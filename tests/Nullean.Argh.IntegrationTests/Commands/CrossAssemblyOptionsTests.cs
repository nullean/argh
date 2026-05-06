using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Commands;

/// <summary>
/// Regression tests for cross-assembly [AsParameters] DTO types.
/// Non-nullable init properties with C# initializers must NOT be treated as required when
/// the DTO type is defined in a referenced assembly (DeclaringSyntaxReferences is empty in
/// the consuming compilation).
/// </summary>
public class CrossAssemblyOptionsTests
{
	[Fact]
	public void Cross_assembly_as_params_non_nullable_defaults_used_when_flags_absent()
	{
		var result = CliHostRunner.Run("cross-assembly-echo");
		result.ExitCode.Should().Be(0, because: "non-nullable [AsParameters] init properties with defaults must not be required when type is cross-assembly");
		CliHostRunner.StdoutText(result).Trim().Should().Be("cross-assembly:Information:default-tag");
	}

	[Fact]
	public void Cross_assembly_as_params_flag_overrides_default()
	{
		var result = CliHostRunner.Run("cross-assembly-echo", "--level", "Warning", "--tag", "custom");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("cross-assembly:Warning:custom");
	}
}
