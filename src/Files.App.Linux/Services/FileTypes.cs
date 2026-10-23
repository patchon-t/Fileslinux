// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Media;
using FluentIcons.Common;

namespace Files.App.Linux.Services;

public enum FileKind
{
	Folder,
	File,
	Image,
	Video,
	Audio,
	Archive,
	Document,
	Spreadsheet,
	Presentation,
	Pdf,
	Text,
	Code,
	Executable,
	Font,
	DiskImage,
	Package,
}

public sealed record FileTypeInfo(FileKind Kind, Symbol Icon, IBrush Brush, string Description);

/// <summary>Maps file extensions to icons, colors, and descriptions.</summary>
public static class FileTypes
{
	private static readonly IBrush FolderBrush = new SolidColorBrush(Color.Parse("#FFC83D"));
	private static readonly IBrush GrayBrush = new SolidColorBrush(Color.Parse("#8A8A8A"));
	private static readonly IBrush ImageBrush = new SolidColorBrush(Color.Parse("#2E9BD6"));
	private static readonly IBrush VideoBrush = new SolidColorBrush(Color.Parse("#C239B3"));
	private static readonly IBrush AudioBrush = new SolidColorBrush(Color.Parse("#E3008C"));
	private static readonly IBrush ArchiveBrush = new SolidColorBrush(Color.Parse("#D29200"));
	private static readonly IBrush WordBrush = new SolidColorBrush(Color.Parse("#2B579A"));
	private static readonly IBrush ExcelBrush = new SolidColorBrush(Color.Parse("#217346"));
	private static readonly IBrush PowerPointBrush = new SolidColorBrush(Color.Parse("#D24726"));
	private static readonly IBrush PdfBrush = new SolidColorBrush(Color.Parse("#D13438"));
	private static readonly IBrush CodeBrush = new SolidColorBrush(Color.Parse("#0F7B0F"));
	private static readonly IBrush ExecBrush = new SolidColorBrush(Color.Parse("#5C2D91"));
	private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#6B6B6B"));

	public static FileTypeInfo Folder { get; } = new(FileKind.Folder, Symbol.Folder, FolderBrush, "File folder");

	private static readonly Dictionary<string, (FileKind Kind, string Description)> Map = Build();

	private static Dictionary<string, (FileKind, string)> Build()
	{
		var d = new Dictionary<string, (FileKind, string)>(StringComparer.OrdinalIgnoreCase);
		void Add(FileKind kind, string description, params string[] exts)
		{
			foreach (var e in exts)
				d["." + e] = (kind, description);
		}

		Add(FileKind.Image, "Image", "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico", "tif", "tiff", "heic", "heif", "avif", "svg", "raw", "cr2", "nef", "xcf", "psd");
		Add(FileKind.Video, "Video", "mp4", "mkv", "webm", "avi", "mov", "wmv", "flv", "m4v", "mpg", "mpeg", "3gp", "ogv");
		Add(FileKind.Audio, "Audio", "mp3", "flac", "wav", "ogg", "oga", "opus", "m4a", "aac", "wma", "aiff", "mid", "midi");
		Add(FileKind.Archive, "Archive", "zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "tbz2", "xz", "txz", "zst", "lz", "lzma", "cab");
		Add(FileKind.Document, "Document", "doc", "docx", "odt", "rtf", "pages", "epub");
		Add(FileKind.Spreadsheet, "Spreadsheet", "xls", "xlsx", "ods", "csv", "tsv", "numbers");
		Add(FileKind.Presentation, "Presentation", "ppt", "pptx", "odp", "key");
		Add(FileKind.Pdf, "PDF document", "pdf");
		Add(FileKind.Text, "Text document", "txt", "md", "markdown", "log", "ini", "conf", "cfg", "nfo", "rst");
		Add(FileKind.Code, "Source code", "cs", "c", "h", "cpp", "hpp", "cc", "py", "js", "ts", "tsx", "jsx", "java", "kt", "go", "rs", "rb", "php", "swift",
			"sh", "bash", "zsh", "fish", "ps1", "html", "htm", "css", "scss", "json", "xml", "yaml", "yml", "toml", "sql", "lua", "pl", "r", "dart",
			"vue", "svelte", "xaml", "axaml", "csproj", "sln", "slnx", "props", "targets", "gradle", "cmake", "mk", "makefile", "dockerfile");
		Add(FileKind.Executable, "Application", "appimage", "run", "bin", "exe", "msi", "desktop", "so", "elf");
		Add(FileKind.Package, "Package", "deb", "rpm", "flatpak", "snap", "apk", "pkg");
		Add(FileKind.Font, "Font", "ttf", "otf", "woff", "woff2");
		Add(FileKind.DiskImage, "Disk image", "iso", "img", "dmg", "vhd", "vhdx", "qcow2", "vmdk");
		return d;
	}

	public static FileTypeInfo Get(string extension)
	{
		if (string.IsNullOrEmpty(extension) || !Map.TryGetValue(extension, out var entry))
		{
			var desc = string.IsNullOrEmpty(extension) ? "File" : extension.TrimStart('.').ToUpperInvariant() + " file";
			return new FileTypeInfo(FileKind.File, Symbol.Document, GrayBrush, desc);
		}

		var (kind, description) = entry;
		var ext = extension.TrimStart('.').ToUpperInvariant();
		description = kind is FileKind.Text or FileKind.Code or FileKind.Archive ? $"{ext} {description.ToLowerInvariant()}" : description;
		if (kind is FileKind.Image or FileKind.Video or FileKind.Audio)
			description = $"{ext} {description.ToLowerInvariant()}";

		return kind switch
		{
			FileKind.Image => new(kind, Symbol.Image, ImageBrush, description),
			FileKind.Video => new(kind, Symbol.Video, VideoBrush, description),
			FileKind.Audio => new(kind, Symbol.MusicNote2, AudioBrush, description),
			FileKind.Archive => new(kind, Symbol.FolderZip, ArchiveBrush, description),
			FileKind.Document => new(kind, Symbol.DocumentText, WordBrush, description),
			FileKind.Spreadsheet => new(kind, Symbol.DocumentTable, ExcelBrush, description),
			FileKind.Presentation => new(kind, Symbol.SlideLayout, PowerPointBrush, description),
			FileKind.Pdf => new(kind, Symbol.DocumentPdf, PdfBrush, description),
			FileKind.Text => new(kind, Symbol.DocumentText, TextBrush, description),
			FileKind.Code => new(kind, Symbol.Code, CodeBrush, description),
			FileKind.Executable => new(kind, Symbol.WindowConsole, ExecBrush, description),
			FileKind.Package => new(kind, Symbol.Box, ArchiveBrush, description),
			FileKind.Font => new(kind, Symbol.TextFont, TextBrush, description),
			FileKind.DiskImage => new(kind, Symbol.HardDrive, GrayBrush, description),
			_ => new(kind, Symbol.Document, GrayBrush, description),
		};
	}

	public static bool IsArchive(string path)
	{
		var name = path.ToLowerInvariant();
		return name.EndsWith(".zip") || name.EndsWith(".tar") || name.EndsWith(".tar.gz") || name.EndsWith(".tgz");
	}

	public static bool IsTextPreviewable(string extension)
		=> Map.TryGetValue(extension, out var e) ? e.Kind is FileKind.Text or FileKind.Code || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) : string.IsNullOrEmpty(extension);
}
