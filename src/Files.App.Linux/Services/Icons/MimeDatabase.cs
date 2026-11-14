// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Linux.Services.Icons;

/// <summary>
/// Maps file names to MIME types and MIME types to icon names, using the freedesktop.org shared-mime-info
/// database (mime/globs2, mime/generic-icons, mime/icons) that every Linux desktop ships.
/// </summary>
public sealed class MimeDatabase
{
	private readonly Dictionary<string, string> _byExtension = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _genericIcons = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _icons = new(StringComparer.Ordinal);

	private static MimeDatabase? _default;

	public static MimeDatabase Default => _default ??= Load(DataDirectories());

	/// <summary>$XDG_DATA_HOME followed by $XDG_DATA_DIRS (default /usr/local/share:/usr/share).</summary>
	public static IReadOnlyList<string> DataDirectories()
	{
		var dirs = new List<string> { XdgPaths.DataHome };
		var env = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
		dirs.AddRange((string.IsNullOrEmpty(env) ? "/usr/local/share:/usr/share" : env).Split(':', StringSplitOptions.RemoveEmptyEntries));
		return dirs.Distinct().ToList();
	}

	public static MimeDatabase Load(IEnumerable<string> dataDirs)
	{
		var db = new MimeDatabase();
		var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		// Later directories have lower priority, so read them first and let earlier ones override.
		foreach (var dir in dataDirs.Reverse())
		{
			var mime = Path.Combine(dir, "mime");
			db.ReadGlobs(Path.Combine(mime, "globs2"), weights);
			ReadPairs(Path.Combine(mime, "generic-icons"), db._genericIcons);
			ReadPairs(Path.Combine(mime, "icons"), db._icons);
		}
		return db;
	}

	private void ReadGlobs(string file, Dictionary<string, int> weights)
	{
		if (!File.Exists(file))
			return;
		try
		{
			foreach (var line in File.ReadLines(file))
			{
				if (line.Length == 0 || line[0] == '#')
					continue;
				// weight:type:glob[:flags]
				var parts = line.Split(':');
				if (parts.Length < 3 || !int.TryParse(parts[0], out var weight))
					continue;
				var glob = parts[2];
				if (!glob.StartsWith("*.", StringComparison.Ordinal) || glob.IndexOfAny(['*', '?', '['], 2) >= 0)
					continue;
				var ext = glob[1..];
				if (!weights.TryGetValue(ext, out var existing) || weight >= existing)
				{
					weights[ext] = weight;
					_byExtension[ext] = parts[1];
				}
			}
		}
		catch
		{
			// Unreadable database: icons fall back to generic ones.
		}
	}

	private static void ReadPairs(string file, Dictionary<string, string> target)
	{
		if (!File.Exists(file))
			return;
		try
		{
			foreach (var line in File.ReadLines(file))
			{
				var i = line.IndexOf(':');
				if (i > 0)
					target[line[..i]] = line[(i + 1)..].Trim();
			}
		}
		catch
		{
			// Ignore.
		}
	}

	/// <summary>Adds or replaces an extension mapping (used by tests and as a fallback).</summary>
	public void Add(string extension, string mimeType) => _byExtension[extension.StartsWith('.') ? extension : "." + extension] = mimeType;

	/// <summary>MIME type for a file name, from its longest known extension (".tar.gz" before ".gz").</summary>
	public string? MimeTypeFor(string fileName)
	{
		var name = Path.GetFileName(fileName);
		for (int i = name.IndexOf('.'); i >= 0 && i < name.Length - 1; i = name.IndexOf('.', i + 1))
		{
			if (_byExtension.TryGetValue(name[i..], out var mime))
				return mime;
		}
		return null;
	}

	/// <summary>Icon names to try for a MIME type, most specific first (e.g. application-pdf, x-office-document).</summary>
	public IEnumerable<string> IconNamesFor(string mimeType)
	{
		if (_icons.TryGetValue(mimeType, out var custom))
			yield return custom;
		yield return mimeType.Replace('/', '-');
		if (_genericIcons.TryGetValue(mimeType, out var generic))
			yield return generic;
		var major = mimeType.Split('/')[0];
		yield return major + "-x-generic";
	}
}
