using FluentAssertions;
using Nullean.Argh.Tests.Fixtures;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Binding;

public class ArghTryParseTests
{
	[Fact]
	public void ArghTryParse_static_on_options_type_binds_global_flags()
	{
		var ok = TestGlobalCliOptions.ArghTryParse(["--verbose"], out var o);
		ok.Should().BeTrue();
		o.Should().NotBeNull();
		o.Verbose.Should().BeTrue();
	}

	[Fact]
	public void ArghTryParse_static_on_command_namespace_options_binds_inherited_and_namespace_flags()
	{
		var ok = TestStorageCommandNamespaceOptions.ArghTryParse(
			["--verbose", "--prefix", "pre"],
			out var s);
		ok.Should().BeTrue();
		s.Should().NotBeNull();
		s.Verbose.Should().BeTrue();
		s.Prefix.Should().Be("pre");
	}

	[Fact]
	public void ArghTryParse_static_on_AsParameters_record_binds_prefixed_flags()
	{
		var ok = DeployCliArgs.ArghTryParse(
			["--app-env", "prod", "--app-port", "8080"],
			out var d);
		ok.Should().BeTrue();
		d.Should().NotBeNull();
		d.Env.Should().Be("prod");
		d.Port.Should().Be(8080);
	}

	[Fact]
	public void ArghTryParse_Type_extension_still_works_for_generic_dispatch()
	{
		var ok = typeof(TestGlobalCliOptions).ArghTryParse<TestGlobalCliOptions>(["--verbose"], out var o);
		ok.Should().BeTrue();
		o!.Verbose.Should().BeTrue();
	}
}
