namespace Nullean.Argh.Tests.Fixtures;

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
