// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Files.App.Linux.Services.Icons;

/// <summary>The icons.json file of an icon pack.</summary>
public sealed class IconPackManifest
{
	public string? Name { get; set; }
	public string? Author { get; set; }
	public string? Description { get; set; }

	/// <summary>What to use for icons the pack doesn't define: "builtin" (default), "system", or "theme:NAME".</summary>
	public string? Fallback { get; set; }

	/// <summary>Key → image file (PNG or SVG) relative to the pack folder. See docs/linux-icons.md for the keys.</summary>
	public Dictionary<string, string> Icons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IconPackManifest))]
internal sealed partial class IconPackJsonContext : JsonSerializerContext;

/// <summary>
/// A user icon pack: a folder with icons.json and image files. One pack changes the icons everywhere in Files
/// (file list, sidebar, Home, details pane, properties) and can be installed as a system icon theme for other apps.
/// </summary>
public sealed class IconPack
{
	public const string ManifestFile = "icons.json";

	private readonly Dictionary<string, string> _icons;

	public string Folder { get; }

	public string Id => Path.GetFileName(Folder);

	public IconPackManifest Manifest { get; }

	public string DisplayName => Manifest.Name ?? Id;

	private IconPack(string folder, IconPackManifest manifest)
	{
		Folder = folder;
		Manifest = manifest;
		_icons = new Dictionary<string, string>(manifest.Icons, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>Where packs live: ~/.local/share/files/icon-packs/NAME/icons.json.</summary>
	public static string PacksDirectory => Path.Combine(XdgPaths.DataHome, "files", "icon-packs");

	public static IconPack? Load(string folder)
	{
		var file = Path.Combine(folder, ManifestFile);
		if (!File.Exists(file))
			return null;
		try
		{
			var manifest = JsonSerializer.Deserialize(File.ReadAllText(file), IconPackJsonContext.Default.IconPackManifest);
			return manifest is null ? null : new IconPack(folder, manifest);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static IReadOnlyList<IconPack> Installed()
	{
		if (!Directory.Exists(PacksDirectory))
			return [];
		return Directory.EnumerateDirectories(PacksDirectory)
			.Select(Load)
			.Where(p => p is not null)
			.Cast<IconPack>()
			.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	/// <summary>Full path of the image for the first key that the pack defines (and whose file exists).</summary>
	public string? Resolve(IEnumerable<string> keys)
	{
		foreach (var key in keys)
		{
			if (_icons.TryGetValue(key, out var relative))
			{
				var full = Path.GetFullPath(Path.Combine(Folder, relative));
				// Stay inside the pack folder.
				if (full.StartsWith(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(full))
					return full;
			}
		}
		return null;
	}

	/// <summary>
	/// Imports a pack from a .zip or from an icons.json file (its folder is copied). Returns the installed pack.
	/// </summary>
	public static IconPack Import(string path)
	{
		Directory.CreateDirectory(PacksDirectory);
		string source;
		string? tempDir = null;
		if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			tempDir = Path.Combine(Path.GetTempPath(), "files-iconpack-" + Guid.NewGuid().ToString("N"));
			ZipFile.ExtractToDirectory(path, tempDir);
			// The manifest can be at the root or in one top-level folder.
			var manifest = Directory.EnumerateFiles(tempDir, ManifestFile, SearchOption.AllDirectories).OrderBy(f => f.Length).FirstOrDefault()
				?? throw new InvalidDataException($"The zip file has no {ManifestFile}.");
			source = Path.GetDirectoryName(manifest)!;
		}
		else if (Path.GetFileName(path).Equals(ManifestFile, StringComparison.OrdinalIgnoreCase))
		{
			source = Path.GetDirectoryName(path)!;
		}
		else if (Directory.Exists(path) && File.Exists(Path.Combine(path, ManifestFile)))
		{
			source = path;
		}
		else
		{
			throw new InvalidDataException($"Choose a .zip icon pack or its {ManifestFile} file.");
		}

		try
		{
			var pack = Load(source) ?? throw new InvalidDataException($"{ManifestFile} could not be read.");
			var id = SafeId(pack.Manifest.Name ?? Path.GetFileNameWithoutExtension(path));
			var target = Path.Combine(PacksDirectory, id);
			if (Path.GetFullPath(source) != Path.GetFullPath(target))
			{
				if (Directory.Exists(target))
					Directory.Delete(target, true);
				CopyDirectory(source, target);
			}
			return Load(target)!;
		}
		finally
		{
			if (tempDir is not null)
				TryDelete(tempDir);
		}
	}

	internal static string SafeId(string name)
	{
		var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
		var id = new string(chars).Trim('-');
		return id.Length == 0 ? "icon-pack" : id;
	}

	private static void CopyDirectory(string from, string to)
	{
		Directory.CreateDirectory(to);
		foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
		{
			var dest = Path.Combine(to, Path.GetRelativePath(from, file));
			Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
			File.Copy(file, dest, true);
		}
	}

	private static void TryDelete(string dir)
	{
		try
		{
			Directory.Delete(dir, true);
		}
		catch
		{
			// Ignore.
		}
	}

	/// <summary>Writes a small, working example pack (with SVG icons) to start from. Returns its folder.</summary>
	public static string CreateExample()
	{
		var folder = Path.Combine(PacksDirectory, "example-pack");
		Directory.CreateDirectory(Path.Combine(folder, "icons"));
		void Svg(string name, string body) => File.WriteAllText(Path.Combine(folder, "icons", name), "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 48 48\">" + body + "</svg>");

		Svg("folder.svg", "<path d=\"M4 10a3 3 0 0 1 3-3h11l4 4h19a3 3 0 0 1 3 3v24a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3z\" fill=\"#7c4dff\"/><path d=\"M4 17a3 3 0 0 1 3-3h34a3 3 0 0 1 3 3v19a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3z\" fill=\"#b388ff\"/>");
		Svg("file.svg", "<path d=\"M11 4h18l10 10v28a2 2 0 0 1-2 2H11a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z\" fill=\"#eceff1\" stroke=\"#90a4ae\" stroke-width=\"1.5\"/><path d=\"M29 4v10h10\" fill=\"#cfd8dc\"/>");
		Svg("pdf.svg", "<path d=\"M11 4h18l10 10v28a2 2 0 0 1-2 2H11a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z\" fill=\"#ffebee\" stroke=\"#e53935\" stroke-width=\"1.5\"/><text x=\"24\" y=\"34\" font-size=\"11\" font-family=\"sans-serif\" font-weight=\"bold\" text-anchor=\"middle\" fill=\"#e53935\">PDF</text>");
		Svg("image.svg", "<rect x=\"6\" y=\"9\" width=\"36\" height=\"30\" rx=\"4\" fill=\"#e3f2fd\" stroke=\"#1e88e5\" stroke-width=\"1.5\"/><circle cx=\"17\" cy=\"19\" r=\"4\" fill=\"#ffb300\"/><path d=\"M8 36l11-11 7 7 5-5 9 9z\" fill=\"#43a047\"/>");
		Svg("home.svg", "<path d=\"M24 6 4 22h6v18h10V30h8v10h10V22h6z\" fill=\"#7c4dff\"/>");
		Svg("trash.svg", "<path d=\"M16 8h16l1 4h8v4H7v-4h8z\" fill=\"#78909c\"/><path d=\"M10 18h28l-2 24H12z\" fill=\"#b0bec5\"/>");

		var manifest = new IconPackManifest
		{
			Name = "Example pack",
			Author = "You",
			Description = "Edit icons.json and the images in icons/ to make it yours.",
			Fallback = "builtin",
			Icons = new(StringComparer.OrdinalIgnoreCase)
			{
				["folder"] = "icons/folder.svg",
				["file"] = "icons/file.svg",
				[".pdf"] = "icons/pdf.svg",
				["kind/image"] = "icons/image.svg",
				["places/home"] = "icons/home.svg",
				["places/trash"] = "icons/trash.svg",
			},
		};
		File.WriteAllText(Path.Combine(folder, ManifestFile), JsonSerializer.Serialize(manifest, IconPackJsonContext.Default.IconPackManifest));
		return folder;
	}

	/// <summary>Freedesktop icon names for a pack key, so the pack can be installed as a system icon theme.</summary>
	private static IEnumerable<(string Context, string Name)> FreedesktopNames(string key, MimeDatabase mime)
	{
		var k = key.ToLowerInvariant();
		if (k == "folder")
		{
			yield return ("places", "folder");
			yield return ("places", "inode-directory");
			yield break;
		}
		if (k == "file")
		{
			yield return ("mimetypes", "text-x-generic");
			yield return ("mimetypes", "application-octet-stream");
			yield return ("mimetypes", "unknown");
			yield break;
		}
		if (k.StartsWith("places/", StringComparison.Ordinal) && IconService.PlaceIconNames.TryGetValue(k[7..], out var placeNames))
		{
			foreach (var n in placeNames)
				yield return (n.StartsWith("drive", StringComparison.Ordinal) ? "devices" : "places", n);
			yield break;
		}
		if (k.StartsWith("mime/", StringComparison.Ordinal))
		{
			yield return ("mimetypes", k[5..].Replace('/', '-'));
			yield break;
		}
		if (k.StartsWith("kind/", StringComparison.Ordinal) && IconService.KindIconNames.TryGetValue(k[5..], out var kindName))
		{
			yield return ("mimetypes", kindName);
			yield break;
		}
		if (k.StartsWith('.') && mime.MimeTypeFor("x" + k) is { } type)
			yield return ("mimetypes", type.Replace('/', '-'));
	}

	/// <summary>
	/// Installs the pack as a freedesktop icon theme in ~/.local/share/icons, so other apps can use it too.
	/// Icons the pack doesn't define come from the system theme it inherits from. Returns the theme name.
	/// </summary>
	public string InstallAsSystemTheme(string? inheritFrom = null, MimeDatabase? mime = null, string? iconsDirectory = null)
	{
		mime ??= MimeDatabase.Default;
		var name = "Files-" + Id;
		var root = Path.Combine(iconsDirectory ?? Path.Combine(XdgPaths.DataHome, "icons"), name);
		if (Directory.Exists(root))
			Directory.Delete(root, true);

		var contexts = new HashSet<string>(StringComparer.Ordinal);
		var bitmapContexts = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (key, relative) in _icons)
		{
			var source = Resolve([key]);
			if (source is null)
				continue;
			var isSvg = source.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
			foreach (var (context, iconName) in FreedesktopNames(key, mime))
			{
				var dir = isSvg ? Path.Combine(root, "scalable", context) : Path.Combine(root, "256x256", context);
				Directory.CreateDirectory(dir);
				File.Copy(source, Path.Combine(dir, iconName + Path.GetExtension(source).ToLowerInvariant()), true);
				(isSvg ? contexts : bitmapContexts).Add(context);
			}
		}

		var inherits = string.Join(",", new[] { inheritFrom, "Adwaita", "hicolor" }.Where(t => !string.IsNullOrEmpty(t) && t != name).Distinct());
		var dirs = contexts.Select(c => "scalable/" + c).Concat(bitmapContexts.Select(c => "256x256/" + c)).ToList();
		var index = new System.Text.StringBuilder();
		index.Append("[Icon Theme]\nName=").Append(DisplayName).Append(" (Files)\nComment=Made from a Files icon pack\nInherits=").Append(inherits)
			.Append("\nDirectories=").Append(string.Join(",", dirs)).Append("\n\n");
		foreach (var c in contexts)
			index.Append("[scalable/").Append(c).Append("]\nSize=48\nMinSize=8\nMaxSize=512\nType=Scalable\nContext=").Append(ContextName(c)).Append("\n\n");
		foreach (var c in bitmapContexts)
			index.Append("[256x256/").Append(c).Append("]\nSize=256\nMinSize=8\nMaxSize=512\nType=Scalable\nContext=").Append(ContextName(c)).Append("\n\n");
		Directory.CreateDirectory(root);
		File.WriteAllText(Path.Combine(root, "index.theme"), index.ToString());
		IconTheme.ClearCache();
		return name;
	}

	private static string ContextName(string dir) => dir switch
	{
		"places" => "Places",
		"devices" => "Devices",
		_ => "MimeTypes",
	};
}
