// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Linux.Services;

/// <summary>freedesktop.org base and user directories.</summary>
public static class XdgPaths
{
	public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } h ? h : (Environment.GetEnvironmentVariable("HOME") ?? "/");

	public static string ConfigHome => Env("XDG_CONFIG_HOME") ?? Path.Combine(Home, ".config");

	public static string DataHome => Env("XDG_DATA_HOME") ?? Path.Combine(Home, ".local", "share");

	private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v && Path.IsPathRooted(v) ? v : null;

	private static Dictionary<string, string>? _userDirs;

	/// <summary>Gets a user directory such as DESKTOP, DOWNLOAD, DOCUMENTS, MUSIC, PICTURES, VIDEOS.</summary>
	public static string GetUserDir(string key)
	{
		_userDirs ??= ParseUserDirs(Path.Combine(ConfigHome, "user-dirs.dirs"), Home);
		if (_userDirs.TryGetValue(key, out var dir))
			return dir;

		return key switch
		{
			"DESKTOP" => Path.Combine(Home, "Desktop"),
			"DOWNLOAD" => Path.Combine(Home, "Downloads"),
			"DOCUMENTS" => Path.Combine(Home, "Documents"),
			"MUSIC" => Path.Combine(Home, "Music"),
			"PICTURES" => Path.Combine(Home, "Pictures"),
			"VIDEOS" => Path.Combine(Home, "Videos"),
			"TEMPLATES" => Path.Combine(Home, "Templates"),
			"PUBLICSHARE" => Path.Combine(Home, "Public"),
			_ => Home,
		};
	}

	/// <summary>Parses the user-dirs.dirs file (lines like XDG_DOWNLOAD_DIR="$HOME/Downloads").</summary>
	internal static Dictionary<string, string> ParseUserDirs(string file, string home)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			if (!File.Exists(file))
				return result;

			foreach (var raw in File.ReadAllLines(file))
			{
				var line = raw.Trim();
				if (line.StartsWith('#') || !line.StartsWith("XDG_", StringComparison.Ordinal))
					continue;

				var eq = line.IndexOf('=');
				if (eq < 0)
					continue;

				var name = line[4..eq];
				if (name.EndsWith("_DIR", StringComparison.Ordinal))
					name = name[..^4];

				var value = line[(eq + 1)..].Trim().Trim('"');
				value = value.Replace("$HOME", home, StringComparison.Ordinal);
				if (!Path.IsPathRooted(value))
					continue;

				result[name] = value.TrimEnd('/') is { Length: > 0 } v ? v : "/";
			}
		}
		catch
		{
			// Ignore unreadable files.
		}
		return result;
	}
}
