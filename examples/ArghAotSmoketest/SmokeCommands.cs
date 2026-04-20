namespace ArghAotSmoketest;

/// <summary>Root commands exercised by <see cref="Microsoft.Extensions.Hosting"/> + Native AOT publish.</summary>
public sealed class SmokeCmds
{
	/// <summary>Smoke ping.</summary>
	public void Ping() => Console.WriteLine("pong");
}

/// <summary>Namespace segment <c>ns</c> (see Program registration).</summary>
public sealed class NsCmds
{
	/// <summary>Nested smoke command.</summary>
	public void Hi() => Console.WriteLine("hi");
}
