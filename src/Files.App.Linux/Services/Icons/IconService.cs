// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Media;
using Avalonia.Media.Imaging;
using Files.App.Linux.Models;
using SkiaSharp;
using Svg.Skia;

namespace Files.App.Linux.Services.Icons;

/// <summary>
/// Chooses the icons for files, folders and places. The style is a setting:
/// <list type="bullet">
/// <item>"builtin" — Files' own Fluent icons (default), drawn by the app.</item>
/// <item>"system" — the desktop's icon theme, the same icons as your other apps.</item>
/// <item>"theme:NAME" — a specific installed icon theme (Papirus, Adwaita, Breeze...).</item>
/// <item>"pack:ID" — a user icon pack (icons.json + images); see docs/linux-icons.md.</item>
/// </list>
/// A null result means "use the built-in icon".
/// </summary>
public static class IconService
{
	public const string Builtin = "builtin";
	public const string System = "system";

	/// <summary>Freedesktop icon names for each place, most specific first.</summary>
	public static readonly IReadOnlyDictionary<string, string[]> PlaceIconNames = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		["home"] = ["user-home", "folder-home"],
		["desktop"] = ["user-desktop", "folder-desktop"],
		["downloads"] = ["folder-download", "folder-downloads"],
		["documents"] = ["folder-documents", "folder-document"],
		["pictures"] = ["folder-pictures", "folder-images"],
		["music"] = ["folder-music"],
		["videos"] = ["folder-videos", "folder-video"],
		["trash"] = ["user-trash"],
		["trash-full"] = ["user-trash-full", "user-trash"],
		["drive"] = ["drive-harddisk", "drive-harddisk-system"],
		["drive-removable"] = ["drive-removable-media", "drive-removable-media-usb", "media-removable", "drive-harddisk"],
		["folder"] = ["folder"],
	};

	/// <summary>Freedesktop generic icon for each kind of file (used for kind/... pack keys).</summary>
	public static readonly IReadOnlyDictionary<string, string> KindIconNames = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["image"] = "image-x-generic",
		["video"] = "video-x-generic",
		["audio"] = "audio-x-generic",
		["archive"] = "package-x-generic",
		["document"] = "x-office-document",
		["spreadsheet"] = "x-office-spreadsheet",
		["presentation"] = "x-office-presentation",
		["pdf"] = "application-pdf",
		["text"] = "text-x-generic",
		["code"] = "text-x-script",
		["executable"] = "application-x-executable",
		["font"] = "font-x-generic",
		["diskimage"] = "application-x-cd-image",
		["package"] = "package-x-generic",
	};

	private static string _style = Builtin;
	private static IconTheme? _theme;
	private static IconPack? _pack;
	private static IconTheme? _packFallbackTheme;
	private static readonly Dictionary<(string Path, int Size), IImage?> Images = [];

	/// <summary>Raised when the icon style changes; icons in the UI rebuild themselves.</summary>
	public static event EventHandler? Changed;

	public static string Style => _style;

	/// <summary>True when something other than the built-in icons is active.</summary>
	public static bool IsCustom => _theme is not null || _pack is not null;

	/// <summary>Name of the system theme that "system" resolved to.</summary>
	public static string? ActiveThemeName => _theme?.Name ?? _packFallbackTheme?.Name;

	public static string? ActivePackName => _pack?.DisplayName;

	public static IconPack? ActivePack => _pack;

	/// <summary>Applies an icon style (see the class summary). Unknown or missing themes/packs fall back to built-in.</summary>
	public static void Apply(string? style)
	{
		style = string.IsNullOrWhiteSpace(style) ? Builtin : style.Trim();
		_style = style;
		_theme = null;
		_pack = null;
		_packFallbackTheme = null;
		lock (Images)
			Images.Clear();

		if (style == System)
			_theme = IconTheme.DetectSystemTheme() is { } name ? IconTheme.Find(name) : null;
		else if (style.StartsWith("theme:", StringComparison.Ordinal))
			_theme = IconTheme.Find(style[6..]);
		else if (style.StartsWith("pack:", StringComparison.Ordinal))
		{
			_pack = IconPack.Load(Path.Combine(IconPack.PacksDirectory, style[5..]));
			var fallback = _pack?.Manifest.Fallback;
			if (fallback == System)
				_packFallbackTheme = IconTheme.DetectSystemTheme() is { } name ? IconTheme.Find(name) : null;
			else if (fallback?.StartsWith("theme:", StringComparison.Ordinal) == true)
				_packFallbackTheme = IconTheme.Find(fallback[6..]);
		}

		Changed?.Invoke(null, EventArgs.Empty);
	}

	/// <summary>Icon for a file or folder at <paramref name="pixelSize"/>, or null for the built-in icon.</summary>
	public static IImage? GetFileIcon(FileItem item, int pixelSize)
	{
		if (!IsCustom)
			return null;
		string? path;
		if (item.IsDirectory)
		{
			path = Find(["folder"], ["folder", "inode-directory"], pixelSize);
		}
		else
		{
			var mime = MimeDatabase.Default.MimeTypeFor(item.Name);
			var packKeys = new List<string>();
			if (item.Extension.Length > 0)
				packKeys.Add(item.Extension.ToLowerInvariant());
			if (mime is not null)
				packKeys.Add("mime/" + mime);
			packKeys.Add("kind/" + item.TypeInfo.Kind.ToString().ToLowerInvariant());
			packKeys.Add("file");

			var themeNames = new List<string>();
			if (mime is not null)
				themeNames.AddRange(MimeDatabase.Default.IconNamesFor(mime));
			if (KindIconNames.TryGetValue(item.TypeInfo.Kind.ToString().ToLowerInvariant(), out var kindName))
				themeNames.Add(kindName);
			themeNames.AddRange(["text-x-generic", "application-octet-stream", "unknown"]);
			path = Find(packKeys, themeNames, pixelSize);
		}
		return path is null ? null : LoadImage(path, pixelSize);
	}

	/// <summary>Icon for a place (home, desktop, downloads, ..., trash, drive), or null for the built-in icon.</summary>
	public static IImage? GetPlaceIcon(string place, int pixelSize)
	{
		if (!IsCustom || !PlaceIconNames.TryGetValue(place, out var names))
			return null;
		var packKeys = new List<string> { "places/" + place };
		if (place == "trash-full")
			packKeys.Add("places/trash");
		if (place is "drive-removable")
			packKeys.Add("places/drive");
		// A pack's plain "folder" icon is a sensible default for folder places, but drives and the trash keep theirs.
		if (place is "desktop" or "downloads" or "documents" or "pictures" or "music" or "videos" or "folder")
			packKeys.Add("folder");
		var path = Find(packKeys, names, pixelSize);
		return path is null ? null : LoadImage(path, pixelSize);
	}

	private static string? Find(IReadOnlyList<string> packKeys, IReadOnlyList<string> themeNames, int size)
	{
		if (_pack is not null)
			return _pack.Resolve(packKeys) ?? _packFallbackTheme?.Lookup(themeNames, size);
		return _theme?.Lookup(themeNames, size);
	}

	/// <summary>Loads a PNG/JPEG/SVG icon at the given size (cached).</summary>
	public static IImage? LoadImage(string path, int pixelSize)
	{
		var key = (path, pixelSize);
		lock (Images)
		{
			if (Images.TryGetValue(key, out var cached))
				return cached;
		}
		IImage? image = null;
		try
		{
			image = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? RenderSvg(path, pixelSize) : LoadBitmap(path, pixelSize);
		}
		catch
		{
			image = null; // Broken file: fall back to the built-in icon.
		}
		lock (Images)
			Images[key] = image;
		return image;
	}

	private static Bitmap LoadBitmap(string path, int pixelSize)
	{
		using var stream = File.OpenRead(path);
		var bitmap = new Bitmap(stream);
		if (bitmap.PixelSize.Width <= pixelSize * 2)
			return bitmap;
		bitmap.Dispose();
		using var again = File.OpenRead(path);
		return Bitmap.DecodeToWidth(again, pixelSize, BitmapInterpolationMode.HighQuality);
	}

	/// <summary>Renders an SVG to a bitmap of <paramref name="pixelSize"/> pixels (aspect ratio kept, centered).</summary>
	internal static Bitmap? RenderSvg(string path, int pixelSize)
	{
		using var svg = new SKSvg();
		var picture = svg.Load(path);
		if (picture is null)
			return null;
		var bounds = picture.CullRect;
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return null;
		using var surface = new SKBitmap(pixelSize, pixelSize, SKColorType.Rgba8888, SKAlphaType.Premul);
		using (var canvas = new SKCanvas(surface))
		{
			canvas.Clear(SKColors.Transparent);
			var scale = Math.Min(pixelSize / bounds.Width, pixelSize / bounds.Height);
			canvas.Translate((pixelSize - bounds.Width * scale) / 2, (pixelSize - bounds.Height * scale) / 2);
			canvas.Scale(scale);
			canvas.Translate(-bounds.Left, -bounds.Top);
			canvas.DrawPicture(picture);
		}
		using var data = surface.Encode(SKEncodedImageFormat.Png, 100);
		using var stream = new MemoryStream(data.ToArray());
		return new Bitmap(stream);
	}
}
