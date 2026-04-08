using FluentAssertions;
using Nullean.Argh.Runtime;
using Nullean.Argh.Tests.Fixtures;
using Xunit;

namespace Nullean.Argh.Tests;

[Collection("Console")]
public class CliIntegrationTests
{
	[Fact]
	public async Task RunAsync_invokes_named_command_with_flag()
	{
		var code = await ArghRuntime.RunAsync(["hello", "--name", "test"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_unknown_command_returns_2()
	{
		var code = await ArghRuntime.RunAsync(["nope"]);
		code.Should().Be(2);
	}

	[Fact]
	public async Task RunAsync_global_and_per_command_filters_run()
	{
		TestsGlobalFilter.InvokeCount = 0;
		TestsPerCommandFilter.InvokeCount = 0;
		var code = await ArghRuntime.RunAsync(["hello", "--name", "t"]);
		code.Should().Be(0);
		TestsGlobalFilter.InvokeCount.Should().Be(1);
		TestsPerCommandFilter.InvokeCount.Should().Be(1);
	}

	[Fact]
	public async Task RunAsync_completions_bash_prints_script()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["--completions", "bash"]);
			code.Should().Be(0);
			sw.ToString().Should().Contain("complete");
		}
		finally
		{
			Console.SetOut(prev);
		}
	}

	[Fact]
	public async Task RunAsync_parses_enum_flags()
	{
		var code = await ArghRuntime.RunAsync(["enum-cmd", "--color", "red", "--name", "x"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_parses_enum_case_insensitive()
	{
		var code = await ArghRuntime.RunAsync(["enum-cmd", "--color", "Blue", "--name", "y"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_grouped_storage_list_routes()
	{
		var code = await ArghRuntime.RunAsync(["storage", "list"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_grouped_nested_blob_upload_routes()
	{
		var code = await ArghRuntime.RunAsync(["storage", "blob", "upload"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_global_then_group_options_parse_order()
	{
		var code = await ArghRuntime.RunAsync(["--verbose", "storage", "--prefix", "p", "list"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_AsParameters_prefixed_flags_bind_record()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["deploy", "--app-env", "prod", "--app-port", "8080"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("deploy:prod:8080");
		}
		finally
		{
			Console.SetOut(prev);
		}
	}

	[Fact]
	public async Task RunAsync_collection_repeated_flags_parse_list()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["tags", "--tags", "a", "--tags", "b"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("tags:a,b");
		}
		finally
		{
			Console.SetOut(prev);
		}
	}

	[Fact]
	public async Task RunAsync_unknown_group_returns_2()
	{
		var code = await ArghRuntime.RunAsync(["nope-group", "x"]);
		code.Should().Be(2);
	}

	[Fact]
	public async Task Help_omits_ansi_when_NO_COLOR_set()
	{
		var prev = Environment.GetEnvironmentVariable("NO_COLOR");
		var stdout = new StringWriter();
		var oldOut = Console.Out;
		try
		{
			Environment.SetEnvironmentVariable("NO_COLOR", "1");
			Console.SetOut(stdout);
			var code = await ArghRuntime.RunAsync(["hello", "--help"]);
			code.Should().Be(0);
			stdout.ToString().Should().NotContain("\x1b");
		}
		finally
		{
			Console.SetOut(oldOut);
			Environment.SetEnvironmentVariable("NO_COLOR", prev);
		}
	}

	[Fact]
	public async Task RunAsync_resolves_AddT_instance_from_ArghServices()
	{
		var prevOut = Console.Out;
		var sw = new StringWriter();
		ArghServices.ServiceProvider = new DiProbeServiceProvider();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["di-probe", "ping"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("probe:from-di");
		}
		finally
		{
			Console.SetOut(prevOut);
			ArghServices.ServiceProvider = null;
		}
	}

	[Fact]
	public void ArghTryParse_static_on_options_type_binds_global_flags()
	{
		var ok = TestGlobalCliOptions.ArghTryParse(["--verbose"], out var o);
		ok.Should().BeTrue();
		o.Should().NotBeNull();
		o!.Verbose.Should().BeTrue();
	}

	[Fact]
	public void ArghTryParse_static_on_command_namespace_options_binds_inherited_and_namespace_flags()
	{
		var ok = TestStorageCommandNamespaceOptions.ArghTryParse(
			["--verbose", "--prefix", "pre"],
			out var s);
		ok.Should().BeTrue();
		s.Should().NotBeNull();
		s!.Verbose.Should().BeTrue();
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
		d!.Env.Should().Be("prod");
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

internal sealed class DiProbeServiceProvider : IServiceProvider
{
	public object? GetService(Type serviceType)
	{
		if (serviceType == typeof(DiProbeCommands))
			return new DiProbeCommands(new DiProbeService());
		if (serviceType == typeof(IDiProbeService))
			return new DiProbeService();
		return null;
	}
}
