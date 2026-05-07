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
		root.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
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
	public void Schema_uses_json_schema_type_primitives_not_csharp_names()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");

		// validate-range has an int? port parameter → type should be "integer"
		var validateRange = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "validate-range");
		var portParam = validateRange.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "port");
		portParam.GetProperty("type").GetString().Should().Be("integer");
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

	[Fact]
	public void Schema_command_with_aliases_exposes_aliases_array()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");

		// renamed-cmd has [CommandName("renamed-cmd", "rc")]
		var renamedCmd = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "renamed-cmd");
		renamedCmd.ValueKind.Should().Be(JsonValueKind.Object);
		renamedCmd.GetProperty("aliases").EnumerateArray()
			.Select(a => a.GetString()).Should().BeEquivalentTo(new[] { "rc" });
	}

	[Fact]
	public void Schema_command_without_aliases_omits_aliases_field()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");

		var helloCmd = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "hello");
		helloCmd.ValueKind.Should().Be(JsonValueKind.Object);
		helloCmd.TryGetProperty("aliases", out _).Should().BeFalse();
	}

	[Fact]
	public void Schema_hidden_command_has_hidden_true()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");

		var hiddenCmd = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "hidden-cmd");
		hiddenCmd.ValueKind.Should().Be(JsonValueKind.Object);
		hiddenCmd.GetProperty("hidden").GetBoolean().Should().BeTrue();

		var visibleCmd = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "visible-cmd");
		visibleCmd.ValueKind.Should().Be(JsonValueKind.Object);
		visibleCmd.TryGetProperty("hidden", out _).Should().BeFalse();
	}

	[Fact]
	public void Schema_hidden_parameter_has_hidden_true()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");

		var cmd = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-hidden-param");
		cmd.ValueKind.Should().Be(JsonValueKind.Object);

		var internalIdParam = cmd.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "internal-id");
		internalIdParam.ValueKind.Should().Be(JsonValueKind.Object);
		internalIdParam.GetProperty("hidden").GetBoolean().Should().BeTrue();

		var nameParam = cmd.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "name");
		nameParam.ValueKind.Should().Be(JsonValueKind.Object);
		nameParam.TryGetProperty("hidden", out _).Should().BeFalse();
	}

	[Fact]
	public void Schema_default_value_is_emitted_for_parameters_with_defaults()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");

		var cmd = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-default-value");
		cmd.ValueKind.Should().Be(JsonValueKind.Object);

		var levelParam = cmd.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "level");
		levelParam.ValueKind.Should().Be(JsonValueKind.Object);
		levelParam.GetProperty("defaultValue").GetString().Should().Be("3");
	}

	[Fact]
	public void Schema_repeatable_collection_has_repeatable_true()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");

		// "tags" command has List<string> tags = repeated flag
		var tagsCmd = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "tags");
		tagsCmd.ValueKind.Should().Be(JsonValueKind.Object);

		var tagsParam = tagsCmd.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "tags");
		tagsParam.ValueKind.Should().Be(JsonValueKind.Object);
		tagsParam.GetProperty("repeatable").GetBoolean().Should().BeTrue();
		tagsParam.GetProperty("type").GetString().Should().Be("array");
		tagsParam.GetProperty("elementType").GetString().Should().Be("string");
	}

	[Fact]
	public void Schema_separator_collection_has_separator_field()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");

		var cmd = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-separator-list");
		cmd.ValueKind.Should().Be(JsonValueKind.Object);

		var idsParam = cmd.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "ids");
		idsParam.ValueKind.Should().Be(JsonValueKind.Object);
		idsParam.GetProperty("separator").GetString().Should().Be(",");
		idsParam.GetProperty("type").GetString().Should().Be("array");
		idsParam.GetProperty("elementType").GetString().Should().Be("integer");
		idsParam.TryGetProperty("repeatable", out _).Should().BeFalse();
	}

	[Fact]
	public void Schema_enum_parameter_has_type_enum_and_enum_values()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var commands = doc.RootElement.GetProperty("commands");

		// "enum-cmd" has an enum parameter
		var enumCmd = commands.EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "enum-cmd");
		enumCmd.ValueKind.Should().Be(JsonValueKind.Object);

		var enumParam = enumCmd.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("type").GetString() == "enum");
		enumParam.ValueKind.Should().Be(JsonValueKind.Object);
		enumParam.GetProperty("enumValues").EnumerateArray().Should().NotBeEmpty();
	}

	[Fact]
	public void Schema_deprecated_command_without_message_emits_deprecated_object()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var cmd = doc.RootElement.GetProperty("commands").EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-deprecated-simple");
		cmd.ValueKind.Should().Be(JsonValueKind.Object);
		cmd.TryGetProperty("deprecated", out var dep).Should().BeTrue();
		dep.ValueKind.Should().Be(JsonValueKind.Object);
	}

	[Fact]
	public void Schema_deprecated_command_with_message_emits_deprecated_message()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var cmd = doc.RootElement.GetProperty("commands").EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-deprecated-with-message");
		cmd.ValueKind.Should().Be(JsonValueKind.Object);
		cmd.TryGetProperty("deprecated", out var dep).Should().BeTrue();
		dep.GetProperty("message").GetString().Should().Contain("schema-deprecated-replacement");
	}

	[Fact]
	public void Schema_deprecated_parameter_emits_deprecated_with_message()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var cmd = doc.RootElement.GetProperty("commands").EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-deprecated-param");
		var oldNameParam = cmd.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "old-name");
		oldNameParam.TryGetProperty("deprecated", out var dep).Should().BeTrue();
		dep.GetProperty("message").GetString().Should().Contain("--name");
	}

	[Fact]
	public void Schema_command_with_intent_emits_intent_object()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var cmd = doc.RootElement.GetProperty("commands").EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-intent-destructive");
		cmd.ValueKind.Should().Be(JsonValueKind.Object);
		var intent = cmd.GetProperty("intent");
		intent.GetProperty("destructive").GetBoolean().Should().BeTrue();
		intent.GetProperty("requiresConfirmation").GetBoolean().Should().BeTrue();
		intent.GetProperty("requiresAuth").GetBoolean().Should().BeTrue();
		intent.GetProperty("scope").GetString().Should().Be("global");
		intent.TryGetProperty("idempotent", out _).Should().BeFalse();
	}

	[Fact]
	public void Schema_confirmationSkip_parameter_has_correct_role()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var cmd = doc.RootElement.GetProperty("commands").EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-intent-destructive");
		var yesParam = cmd.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "yes");
		yesParam.GetProperty("role").GetString().Should().Be("confirmationSkip");
	}

	[Fact]
	public void Schema_dryRun_parameter_has_correct_role()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var cmd = doc.RootElement.GetProperty("commands").EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-intent-read");
		var dryRunParam = cmd.GetProperty("parameters").EnumerateArray()
			.FirstOrDefault(p => p.GetProperty("name").GetString() == "dry-run");
		dryRunParam.GetProperty("role").GetString().Should().Be("dryRun");
	}

	[Fact]
	public void Schema_command_with_output_formats_emits_output_object()
	{
		var result = CliHostRunner.Run("__schema");
		result.ExitCode.Should().Be(0);
		using var doc = JsonDocument.Parse(CliHostRunner.StdoutText(result));
		var cmd = doc.RootElement.GetProperty("commands").EnumerateArray()
			.FirstOrDefault(c => c.GetProperty("name").GetString() == "schema-output-formats");
		cmd.ValueKind.Should().Be(JsonValueKind.Object);
		var output = cmd.GetProperty("output");
		output.GetProperty("formatFlag").GetString().Should().Be("--format");
		var formats = output.GetProperty("formats").EnumerateArray().Select(f => f.GetString()).ToArray();
		formats.Should().BeEquivalentTo(new[] { "json", "table", "csv" });
	}
}
