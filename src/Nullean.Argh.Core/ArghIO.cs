using System.IO;

namespace Nullean.Argh;

/// <summary>Filesystem helpers for CLI validation (symbolic links, reparse points).</summary>
public static class ArghIO
{
	/// <summary>
	/// Returns whether the path refers to a symbolic link or other reparse point (Windows).
	/// Does not require the link target to exist.
	/// </summary>
	public static bool PathIsSymbolicOrReparsePoint(string fullPath)
	{
		if (string.IsNullOrEmpty(fullPath))
			return false;

		try
		{
			var attr = File.GetAttributes(fullPath);
			return (attr & FileAttributes.ReparsePoint) != 0;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}
}
