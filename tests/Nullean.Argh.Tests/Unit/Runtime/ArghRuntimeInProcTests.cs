using FluentAssertions;
using Nullean.Argh.Runtime;
using Nullean.Argh.Tests.Fixtures;
using Xunit;

namespace Nullean.Argh.Tests.Unit.Runtime;

[Collection("Console")]
public class ArghRuntimeInProcTests
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
	public async Task RunAsync_completions_bash_prints_script()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["__completion", "bash"]);
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
	public async Task RunAsync_global_bool_after_namespace_command_is_bare_switch()
	{
		var code = await ArghRuntime.RunAsync(["storage", "list", "--verbose"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_short_global_verbose_before_subcommand_peels_without_value()
	{
		var code = await ArghRuntime.RunAsync(["-v", "storage", "--prefix", "p", "list"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_short_global_verbose_after_namespace_command_is_bare_switch()
	{
		var code = await ArghRuntime.RunAsync(["storage", "list", "-v"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_global_enum_long_form_accepted_after_command_flags()
	{
		var code = await ArghRuntime.RunAsync(["hello", "--name", "t", "--severity", "trace"]);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_global_mode_short_after_command_flags_binds_value()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["hello", "--name", "t", "-m", "alpha"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("ok:t:alpha");
		}
		finally
		{
			Console.SetOut(prev);
		}
	}

	[Fact]
	public async Task RunAsync_global_mode_long_after_command_flags_binds_value()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["hello", "--name", "t", "--mode", "beta"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("ok:t:beta");
		}
		finally
		{
			Console.SetOut(prev);
		}
	}

	[Fact]
	public async Task RunAsync_global_mode_short_equals_form_after_command_flags_binds_value()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["hello", "--name", "t", "-m=gamma"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("ok:t:gamma");
		}
		finally
		{
			Console.SetOut(prev);
		}
	}

	[Fact]
	public async Task RunAsync_global_mode_short_before_command_matches_post_command_form()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["hello", "-m", "delta", "--name", "t"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("ok:t:delta");
		}
		finally
		{
			Console.SetOut(prev);
		}
	}

	[Fact]
	public async Task RunAsync_namespace_command_accepts_global_short_after_namespace_flags()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["storage", "list", "--prefix", "p", "-m", "ns"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("storage-list:ns");
		}
		finally
		{
			Console.SetOut(prev);
		}
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
			var code = await ArghRuntime.RunAsync(["ping"]);
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
	public async Task CommandName_attribute_overrides_method_name()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["renamed-cmd"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("marker:renamed-cmd");
		}
		finally
		{
			Console.SetOut(prev);
		}
	}

	[Fact]
	public async Task CommandName_attribute_original_method_name_not_routable()
	{
		var code = await ArghRuntime.RunAsync(["original-method-name"]);
		code.Should().Be(2);
	}

	[Fact]
	public async Task RunAsync_readonly_set_int_repeated_flags_unique()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["tag-set", "--tag-ids", "3", "--tag-ids", "1", "--tag-ids", "2"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("tag-set:1,2,3");
		}
		finally { Console.SetOut(prev); }
	}

	[Fact]
	public async Task RunAsync_readonly_set_int_duplicate_returns_2()
	{
		var prev = Console.Error;
		var sw = new StringWriter();
		try
		{
			Console.SetError(sw);
			var code = await ArghRuntime.RunAsync(["tag-set", "--tag-ids", "1", "--tag-ids", "1"]);
			code.Should().Be(2);
			sw.ToString().Should().Contain("duplicate");
		}
		finally { Console.SetError(prev); }
	}

	[Fact]
	public async Task RunAsync_readonly_set_enum_repeated_flags_unique()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["color-set", "--colors", "Blue", "--colors", "Red"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("color-set:Red,Blue");
		}
		finally { Console.SetOut(prev); }
	}

	[Fact]
	public async Task RunAsync_nullable_readonly_set_omitted_is_null()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["opt-tag-set"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("opt-tag-set:null");
		}
		finally { Console.SetOut(prev); }
	}

	[Fact]
	public async Task RunAsync_nullable_readonly_set_with_values()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["opt-tag-set", "--tag-ids", "5", "--tag-ids", "10"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("opt-tag-set:5,10");
		}
		finally { Console.SetOut(prev); }
	}

	[Fact]
	public async Task RunAsync_as_params_readonly_set_binds()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["as-params-tag-set", "--tag-ids", "7", "--tag-ids", "3"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("as-params-tag-set:3,7");
		}
		finally { Console.SetOut(prev); }
	}

	[Fact]
	public async Task RunAsync_root_MapAndRootAlias_command_flag_prefetches_before_dispatch()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["--prefetch-regression"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("marker:root-default:prefetch");
		}
		finally { Console.SetOut(prev); }
	}

	[Fact]
	public async Task RunAsync_alias_followed_namespace_default_routes_to_alias_target()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["alias-followed"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("marker:alias-followed-build");
		}
		finally { Console.SetOut(prev); }
	}

	[Fact]
	public async Task RunAsync_alias_followed_namespace_first_followup_command_is_routable()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["alias-followed", "diff"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("marker:alias-followed-diff");
		}
		finally { Console.SetOut(prev); }
	}

	[Fact]
	public async Task RunAsync_alias_followed_namespace_second_followup_command_is_routable()
	{
		var prev = Console.Out;
		var sw = new StringWriter();
		try
		{
			Console.SetOut(sw);
			var code = await ArghRuntime.RunAsync(["alias-followed", "serve"]);
			code.Should().Be(0);
			sw.ToString().Trim().Should().Be("marker:alias-followed-serve");
		}
		finally { Console.SetOut(prev); }
	}
}
