using FluentAssertions;
using Nullean.Argh;
using Xunit;

namespace Nullean.Make.Tests;

public record GlobalOptionHelpTestOptions(int Retries = 1);

public class GlobalOptionHelpBuild : MakeBuild
{
	[GlobalOption("--token", Description = "API token")]
	public string? Token { get; set; }

	[GlobalOption("--parallel", Description = "Worker count")]
	public int Parallel { get; set; }

	[GlobalOption("--ratio", Description = "Sampling ratio")]
	public double? Ratio { get; set; }

	public Target<GlobalOptionHelpTestOptions> Test => _ => _.Executes(_ => { });
}

[Collection(nameof(ConsoleCollection))]
public class MakeAppGlobalOptionHelpTests
{
	private static async Task<string> HelpAsync(params string[] args)
	{
		var stdout = new StringWriter();
		var oldOut = Console.Out;
		Console.SetOut(stdout);
		try
		{
			(await MakeApp.Execute<GlobalOptionHelpBuild>(args)).Should().Be(0);
			return stdout.ToString();
		}
		finally
		{
			Console.SetOut(oldOut);
		}
	}

	[Fact]
	public async Task Global_options_show_their_property_type()
	{
		var help = await HelpAsync("--help");
		help.Should().Contain("--token <string>");
		help.Should().Contain("--parallel <int>");
		help.Should().Contain("--ratio <double>");
	}

	[Fact]
	public async Task Target_options_use_csharp_type_names()
	{
		var help = await HelpAsync("test", "--help");
		help.Should().Contain("--retries <int>");
		help.Should().NotContain("int32");
	}
}
