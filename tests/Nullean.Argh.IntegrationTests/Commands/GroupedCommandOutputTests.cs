using FluentAssertions;
using Nullean.Argh.IntegrationTests.Infrastructure;
using Xunit;

namespace Nullean.Argh.IntegrationTests.Commands;

public class GroupedCommandOutputTests
{
	[Fact]
	public void Storage_list_prints_marker()
	{
		var result = CliHostRunner.Run("storage", "list");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("storage-list");
	}

	[Fact]
	public void Storage_blob_upload_prints_marker()
	{
		var result = CliHostRunner.Run("storage", "blob", "upload");
		result.ExitCode.Should().Be(0);
		CliHostRunner.StdoutText(result).Trim().Should().Be("blob-upload");
	}
}
