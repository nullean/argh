using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Completions;

public class SchemaTests
{
	[Fact]
	public void Schema_stdout_is_json_with_expected_shape()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		var stdout = CliHostRunner.StdoutText(result);
		stdout.Should().NotBeNullOrWhiteSpace();

		using var doc = JsonDocument.Parse(stdout);
		var root = doc.RootElement;
		root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
		root.GetProperty("entryAssembly").GetString().Should().NotBeNullOrWhiteSpace();
		root.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
		root.TryGetProperty("description", out _).Should().BeTrue();
		root.GetProperty("reservedMetaCommands").EnumerateArray().Select(e => e.GetString()).Should()
			.Contain(new[] { "__complete", "__completion", "__schema" });
		root.GetProperty("globalOptions").ValueKind.Should().Be(JsonValueKind.Array);
		root.TryGetProperty("rootDefault", out _).Should().BeTrue();
		root.GetProperty("commands").ValueKind.Should().Be(JsonValueKind.Array);
		root.GetProperty("namespaces").ValueKind.Should().Be(JsonValueKind.Array);
	}

	[Fact]
	public void Schema_validate_range_command_has_range_constraint_on_port()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");
		var validateRange = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "validate-range");
		validateRange.ValueKind.Should().Be(JsonValueKind.Object);

		var portParam = validateRange.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "port");
		portParam.ValueKind.Should().Be(JsonValueKind.Object);

		portParam.GetProperty("validations").ValueKind.Should().Be(JsonValueKind.Array);
		var constraint = portParam.GetProperty("validations").EnumerateArray().First();
		constraint.GetProperty("kind").GetString().Should().Be("range");
		constraint.GetProperty("min").GetString().Should().Be("1");
		constraint.GetProperty("max").GetString().Should().Be("65535");
	}

	[Fact]
	public void Schema_validate_allowed_command_has_allowed_constraint_with_values()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");
		var validateAllowed = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "validate-allowed");
		validateAllowed.ValueKind.Should().Be(JsonValueKind.Object);

		var envParam = validateAllowed.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "env");
		envParam.ValueKind.Should().Be(JsonValueKind.Object);

		var constraint = envParam.GetProperty("validations").EnumerateArray().First();
		constraint.GetProperty("kind").GetString().Should().Be("allowed");
		constraint.GetProperty("values").EnumerateArray().Select(v => v.GetString())
			.Should().BeEquivalentTo(new[] { "\"dev\"", "\"staging\"", "\"prod\"" });
	}
}
