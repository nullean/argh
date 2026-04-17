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
		root.GetProperty("reservedMetaCommands").EnumerateArray().Select(e => e.GetString()).Should()
			.Contain(new[] { "__complete", "__completion", "__schema" });
		root.GetProperty("globalOptions").ValueKind.Should().Be(JsonValueKind.Array);
		root.TryGetProperty("rootDefault", out _).Should().BeTrue();
		root.GetProperty("commands").ValueKind.Should().Be(JsonValueKind.Array);
		root.GetProperty("namespaces").ValueKind.Should().Be(JsonValueKind.Array);
	}
}
