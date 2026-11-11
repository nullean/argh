using AwesomeAssertions;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Nullean.Argh.Generator.Tests;

public class ReferenceMetadataCapabilitiesTests
{
	[Test]
	public void Compute_empty_is_all_false()
	{
		var c = ReferenceMetadataCapabilities.Compute(ImmutableArray<MetadataReference>.Empty);
		c.HasMicrosoftExtensionsDependencyInjection.Should().BeFalse();
		c.HasMicrosoftExtensionsHosting.Should().BeFalse();
	}

	[Test]
	public void Compute_detects_di_and_host_by_display_suffix()
	{
		var baseDir = Path.Combine(Path.GetTempPath(), "argh-refcap-" + Guid.NewGuid());
		Directory.CreateDirectory(baseDir);
		try
		{
			var coreLib = typeof(object).Assembly.Location;
			var diPath = Path.Combine(baseDir, "Microsoft.Extensions.DependencyInjection.dll");
			var hostPath = Path.Combine(baseDir, "Microsoft.Extensions.Hosting.dll");
			File.Copy(coreLib, diPath);
			File.Copy(coreLib, hostPath);
			ImmutableArray<MetadataReference> refs = ImmutableArray.Create<MetadataReference>(
				MetadataReference.CreateFromFile(diPath),
				MetadataReference.CreateFromFile(hostPath));
			var c = ReferenceMetadataCapabilities.Compute(refs);
			c.HasMicrosoftExtensionsDependencyInjection.Should().BeTrue();
			c.HasMicrosoftExtensionsHosting.Should().BeTrue();
		}
		finally
		{
			try
			{
				Directory.Delete(baseDir, recursive: true);
			}
			catch
			{
				// best-effort cleanup on temp paths
			}
		}
	}

	[Test]
	public void Compute_mismatch_suffix_does_not_set_flags()
	{
		var baseDir = Path.Combine(Path.GetTempPath(), "argh-refcap-" + Guid.NewGuid());
		Directory.CreateDirectory(baseDir);
		try
		{
			var coreLib = typeof(object).Assembly.Location;
			var otherPath = Path.Combine(baseDir, "Unrelated.Assembly.dll");
			File.Copy(coreLib, otherPath);
			ImmutableArray<MetadataReference> refs = ImmutableArray.Create<MetadataReference>(
				MetadataReference.CreateFromFile(otherPath));
			var c = ReferenceMetadataCapabilities.Compute(refs);
			c.HasMicrosoftExtensionsDependencyInjection.Should().BeFalse();
			c.HasMicrosoftExtensionsHosting.Should().BeFalse();
		}
		finally
		{
			try
			{
				Directory.Delete(baseDir, recursive: true);
			}
			catch
			{
			}
		}
	}
}
