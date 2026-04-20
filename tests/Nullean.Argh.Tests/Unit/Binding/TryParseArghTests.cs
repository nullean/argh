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
}
