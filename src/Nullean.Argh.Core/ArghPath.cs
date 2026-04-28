using System;
using System.IO;

namespace Nullean.Argh;

/// <summary>Path helpers for CLI binding (user profile expansion, etc.).</summary>
public static class ArghPath
{
	/// <summary>
	/// Expands a leading <c>~/</c> or <c>~\</c> prefix to the current user's profile directory,
	/// treats bare <c>~</c> as the profile directory, then applies <see cref="Path.GetFullPath(string)"/>
	/// so relative segments are resolved against the current working directory.
	/// </summary>
	public static string ExpandUserProfilePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return path;

		var trimmedPath = path.Trim();

		if (trimmedPath.StartsWith("~/", StringComparison.Ordinal) ||
		    trimmedPath.StartsWith("~\\", StringComparison.Ordinal))
		{
			var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			homeDirectory = homeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			trimmedPath = homeDirectory + Path.DirectorySeparatorChar + trimmedPath.Substring(2);
		}
		else if (trimmedPath == "~")
		{
			trimmedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}

		return Path.GetFullPath(trimmedPath);
	}
}
