using FluentAssertions;
using Nullean.Argh.Tests.Fixtures;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Binding;

public class TryParseArghTests
{
	[Fact]
	public void TryParseArgh_static_on_options_type_binds_global_flags()
	{
		var ok = TestGlobalCliOptions.TryParseArgh(["--verbose"], out var o);
		ok.Should().BeTrue();
		o.Should().NotBeNull();
		o.Verbose.Should().BeTrue();
	}

	[Fact]
	public void TryParseArgh_static_on_command_namespace_options_binds_inherited_and_namespace_flags()
	{
		var ok = TestStorageCommandNamespaceOptions.TryParseArgh(
			["--verbose", "--prefix", "pre"],
			out var s);
		ok.Should().BeTrue();
		s.Should().NotBeNull();
		s.Verbose.Should().BeTrue();
		s.Prefix.Should().Be("pre");
	}

	[Fact]
	public void TryParseArgh_static_on_AsParameters_record_binds_prefixed_flags()
	{
		var ok = DeployCliArgs.TryParseArgh(
			["--app-env", "prod", "--app-port", "8080"],
			out var d);
		ok.Should().BeTrue();
		d.Should().NotBeNull();
		d.Env.Should().Be("prod");
		d.Port.Should().Be(8080);
	}

	[Fact]
	public void TryParseArgh_static_on_AsParameters_record_uses_default_CancellationToken_for_injected_member()
	{
		var ok = AsParamsWithCtArgs.TryParseArgh(
			["--run-env", "z", "--run-port", "7"],
			out var a);
		ok.Should().BeTrue();
		a.Should().NotBeNull();
		a.Env.Should().Be("z");
		a.Port.Should().Be(7);
		a.Ct.CanBeCanceled.Should().BeFalse();
	}

	[Fact]
	public void TryParseArgh_static_on_AsParameters_record_binds_nullable_int_flags()
	{
		var ok = NullableNumericAsParamsArgs.TryParseArgh(
			["--labs-rps", "10", "--labs-max-pages", "5"],
			out var a);
		ok.Should().BeTrue();
		a.Should().NotBeNull();
		a.Rps.Should().Be(10);
		a.MaxPages.Should().Be(5);
	}

	[Fact]
	public void TryParseArgh_static_on_AsParameters_omitted_nullable_int_flags_are_null()
	{
		var ok = NullableNumericAsParamsArgs.TryParseArgh([], out var a);
		ok.Should().BeTrue();
		a.Should().NotBeNull();
		a.Rps.Should().BeNull();
		a.MaxPages.Should().BeNull();
	}

	[Fact]
	public void TryParseArgh_Type_extension_still_works_for_generic_dispatch()
	{
		var ok = typeof(TestGlobalCliOptions).TryParseArgh<TestGlobalCliOptions>(["--verbose"], out var o);
		ok.Should().BeTrue();
		o!.Verbose.Should().BeTrue();
	}

	[Fact]
	public void TryParseArgh_global_non_nullable_enum_omitted_uses_property_initializer()
	{
		var ok = TestGlobalCliOptions.TryParseArgh([], out var o);
		ok.Should().BeTrue();
		o.Should().NotBeNull();
		o.Severity.Should().Be(FixtureSeverity.Information);
	}

	[Fact]
	public void TryParseArgh_global_non_nullable_enum_can_be_overridden_by_flag()
	{
		var ok = TestGlobalCliOptions.TryParseArgh(["--severity", "Warning"], out var o);
		ok.Should().BeTrue();
		o.Should().NotBeNull();
		o.Severity.Should().Be(FixtureSeverity.Warning);
	}

	[Fact]
	public void TryParseArgh_mixed_nullability_multi_enum_DTO_parses_flags()
	{
		var ok = MultiEnumAsParamsArgs.TryParseArgh(
			["--mix-severity", "Trace", "--mix-config-source", "Environment"],
			out var a);
		ok.Should().BeTrue();
		a.Should().NotBeNull();
		a.Severity.Should().Be(FixtureSeverity.Trace);
		a.ConfigSource.Should().Be(FixtureConfigSource.Environment);
	}
}
