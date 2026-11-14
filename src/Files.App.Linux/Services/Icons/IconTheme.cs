// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Linux.Services.Icons;

/// <summary>
/// A freedesktop.org icon theme (Adwaita, Papirus, Breeze...), the same icons every other Linux app uses.
/// Implements the Icon Theme Specification lookup: theme, then its Inherits chain, then hicolor.
/// </summary>
public sealed class IconTheme
{
	private sealed record SubDir(string Path, int Size, string Type, int MinSize, int MaxSize, int Threshold, int Scale);

	private static readonly string[] Extensions = [".svg", ".png", ".xpm"];
	private readonly List<string> _baseDirs = [];
	private readonly List<SubDir> _dirs = [];
	private readonly Dictionary<(string, int), string?> _cache = [];

	public string Name { get; }

	public string DisplayName { get; private set; }

	public IReadOnlyList<string> Inherits { get; private set; } = [];

	public bool IsValid => _dirs.Count > 0;

	private IconTheme(string name)
	{
		Name = name;
		DisplayName = name;
	}

	/// <summary>Icon theme base directories: ~/.local/share/icons, ~/.icons, $XDG_DATA_DIRS/icons, /usr/share/pixmaps.</summary>
	public static IReadOnlyList<string> SearchPaths()
	{
		var list = new List<string> { Path.Combine(XdgPaths.Home, ".icons") };
		list.AddRange(MimeDatabase.DataDirectories().Select(d => Path.Combine(d, "icons")));
		return list.Distinct().ToList();
	}

	private static readonly Dictionary<string, IconTheme?> Loaded = new(StringComparer.Ordinal);

	public static IconTheme? Find(string name)
	{
		lock (Loaded)
		{
			if (Loaded.TryGetValue(name, out var theme))
				return theme;
			theme = Load(name, SearchPaths());
			Loaded[name] = theme;
			return theme;
		}
	}

	/// <summary>Forgets loaded themes (after installing a new one).</summary>
	public static void ClearCache()
	{
		lock (Loaded)
			Loaded.Clear();
	}

	internal static IconTheme? Load(string name, IReadOnlyList<string> searchPaths)
	{
		var theme = new IconTheme(name);
		string? index = null;
		foreach (var basePath in searchPaths)
		{
			var dir = Path.Combine(basePath, name);
			if (!Directory.Exists(dir))
				continue;
			theme._baseDirs.Add(dir);
			var candidate = Path.Combine(dir, "index.theme");
			if (index is null && File.Exists(candidate))
				index = candidate;
		}
		if (index is null)
			return null;

		var sections = IniFile.Read(index);
		if (!sections.TryGetValue("Icon Theme", out var main))
			return null;
		theme.DisplayName = main.GetValueOrDefault("Name", name);
		theme.Inherits = main.GetValueOrDefault("Inherits", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var dirNames = (main.GetValueOrDefault("Directories", string.Empty) + "," + main.GetValueOrDefault("ScaledDirectories", string.Empty))
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct();
		foreach (var d in dirNames)
		{
			if (!sections.TryGetValue(d, out var sec) || !int.TryParse(sec.GetValueOrDefault("Size"), out var size))
				continue;
			int Int(string key, int fallback) => int.TryParse(sec.GetValueOrDefault(key), out var v) ? v : fallback;
			theme._dirs.Add(new SubDir(d, size, sec.GetValueOrDefault("Type", "Threshold"), Int("MinSize", size), Int("MaxSize", size), Int("Threshold", 2), Int("Scale", 1)));
		}
		return theme;
	}

	/// <summary>Installed icon themes that contain icons (cursor-only themes are skipped).</summary>
	public static IReadOnlyList<(string Name, string DisplayName)> Installed()
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var basePath in SearchPaths())
		{
			if (!Directory.Exists(basePath))
				continue;
			foreach (var dir in Directory.EnumerateDirectories(basePath))
			{
				var name = Path.GetFileName(dir);
				if (result.ContainsKey(name) || name is "default" or "hicolor" or "locolor")
					continue;
				var index = Path.Combine(dir, "index.theme");
				if (!File.Exists(index))
					continue;
				var sections = IniFile.Read(index);
				if (sections.TryGetValue("Icon Theme", out var main) && main.ContainsKey("Directories") && main.GetValueOrDefault("Hidden") != "true")
					result[name] = main.GetValueOrDefault("Name", name);
			}
		}
		return result.Select(kv => (kv.Key, kv.Value)).OrderBy(t => t.Value, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	/// <summary>Finds the first of <paramref name="names"/> in this theme or the themes it inherits from.</summary>
	public string? Lookup(IEnumerable<string> names, int size)
	{
		var list = names.ToList();
		var visited = new HashSet<string>(StringComparer.Ordinal);
		foreach (var name in list)
		{
			if (LookupInChain(this, name, size, visited) is { } path)
				return path;
			visited.Clear();
		}
		// Last resort, as the spec says: hicolor, then unthemed icons in /usr/share/pixmaps.
		var hicolor = Name == "hicolor" ? null : Find("hicolor");
		foreach (var name in list)
		{
			if (hicolor?.LookupHere(name, size) is { } h)
				return h;
			foreach (var ext in Extensions)
			{
				var pixmap = Path.Combine("/usr/share/pixmaps", name + ext);
				if (File.Exists(pixmap))
					return pixmap;
			}
		}
		return null;
	}

	private static string? LookupInChain(IconTheme theme, string name, int size, HashSet<string> visited)
	{
		if (!visited.Add(theme.Name))
			return null;
		if (theme.LookupHere(name, size) is { } found)
			return found;
		foreach (var parent in theme.Inherits)
		{
			if (Find(parent) is { } p && LookupInChain(p, name, size, visited) is { } inherited)
				return inherited;
		}
		return null;
	}

	/// <summary>Looks in this theme only: an exact size match if there is one, otherwise the closest size.</summary>
	internal string? LookupHere(string name, int size)
	{
		lock (_cache)
		{
			if (_cache.TryGetValue((name, size), out var cached))
				return cached;
		}

		string? best = null;
		var bestDistance = int.MaxValue;
		foreach (var dir in _dirs)
		{
			foreach (var baseDir in _baseDirs)
			{
				foreach (var ext in Extensions)
				{
					var file = Path.Combine(baseDir, dir.Path, name + ext);
					if (!File.Exists(file))
						continue;
					if (ext == ".xpm")
						continue; // Not supported by our image loader.
					var distance = Distance(dir, size);
					if (distance == 0)
					{
						Remember(name, size, file);
						return file;
					}
					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = file;
					}
				}
			}
		}
		Remember(name, size, best);
		return best;
	}

	private void Remember(string name, int size, string? path)
	{
		lock (_cache)
			_cache[(name, size)] = path;
	}

	private static int Distance(SubDir dir, int size)
	{
		var scaled = size / Math.Max(1, dir.Scale);
		return dir.Type switch
		{
			"Fixed" => Math.Abs(dir.Size - scaled),
			"Scalable" => scaled < dir.MinSize ? dir.MinSize - scaled : scaled > dir.MaxSize ? scaled - dir.MaxSize : 0,
			_ => scaled < dir.Size - dir.Threshold ? dir.Size - dir.Threshold - scaled : scaled > dir.Size + dir.Threshold ? scaled - dir.Size - dir.Threshold : 0,
		};
	}

	/// <summary>The icon theme the desktop is using (GNOME/Cinnamon/MATE settings, KDE, XFCE), if it can be found.</summary>
	public static string? DetectSystemTheme()
	{
		var desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty).ToUpperInvariant();

		if (desktop.Contains("KDE", StringComparison.Ordinal))
		{
			var kde = Path.Combine(XdgPaths.ConfigHome, "kdeglobals");
			if (File.Exists(kde) && IniFile.Read(kde).TryGetValue("Icons", out var icons) && icons.TryGetValue("Theme", out var kdeTheme))
				return kdeTheme;
			return "breeze";
		}

		if (desktop.Contains("XFCE", StringComparison.Ordinal))
		{
			var xfce = LauncherService.RunAndRead("xfconf-query", "-c", "xsettings", "-p", "/Net/IconThemeName")?.Trim();
			if (!string.IsNullOrEmpty(xfce))
				return xfce;
		}

		// GNOME, Cinnamon, MATE, Budgie, Pantheon and others use the GNOME interface setting.
		var gsettings = LauncherService.RunAndRead("gsettings", "get", "org.gnome.desktop.interface", "icon-theme")?.Trim().Trim('\'');
		if (!string.IsNullOrEmpty(gsettings))
			return gsettings;

		foreach (var fallback in new[] { "Adwaita", "breeze", "Papirus" })
		{
			if (Find(fallback) is not null)
				return fallback;
		}
		return null;
	}
}

/// <summary>Minimal reader for .ini-style files (index.theme, kdeglobals).</summary>
internal static class IniFile
{
	public static Dictionary<string, Dictionary<string, string>> Read(string path)
	{
		var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
		Dictionary<string, string>? current = null;
		try
		{
			foreach (var raw in File.ReadLines(path))
			{
				var line = raw.Trim();
				if (line.Length == 0 || line[0] is '#' or ';')
					continue;
				if (line[0] == '[' && line[^1] == ']')
				{
					var name = line[1..^1];
					if (!result.TryGetValue(name, out current))
						result[name] = current = new Dictionary<string, string>(StringComparer.Ordinal);
					continue;
				}
				var eq = line.IndexOf('=');
				if (eq > 0 && current is not null)
					current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
			}
		}
		catch
		{
			// Unreadable file.
		}
		return result;
	}
}
