// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.App.Linux.Services;
using FluentIcons.Common;

namespace Files.App.Linux.Models;

/// <summary>A file or folder shown in a layout.</summary>
public sealed partial class FileItem : ObservableObject
{
	public required string Name { get; init; }

	public required string FullPath { get; init; }

	public bool IsDirectory { get; init; }

	public bool IsHidden { get; init; }

	public bool IsSymlink { get; init; }

	public long Size { get; init; }

	public DateTime Modified { get; init; }

	public DateTime Created { get; init; }

	/// <summary>Only set for items listed in the trash.</summary>
	public TrashEntry? TrashEntry { get; init; }

	public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name);

	public FileTypeInfo TypeInfo => IsDirectory ? FileTypes.Folder : FileTypes.Get(Extension);

	public string TypeDescription => TypeInfo.Description;

	public Symbol Icon => TypeInfo.Icon;

	public IBrush IconBrush => TypeInfo.Brush;

	public string SizeText => IsDirectory ? string.Empty : FormatHelpers.FormatSize(Size);

	public string ModifiedText => FormatHelpers.FormatDate(Modified);

	public string CreatedText => FormatHelpers.FormatDate(Created);

	public string? ParentPath => Path.GetDirectoryName(FullPath);

	/// <summary>Location shown for search and trash results.</summary>
	public string LocationText => TrashEntry?.OriginalPath is { } original ? (Path.GetDirectoryName(original) ?? original) : (ParentPath ?? string.Empty);

	/// <summary>Date column in the Details layout: date deleted in the trash, otherwise date modified.</summary>
	public string DateColumnText => TrashEntry is null ? ModifiedText : DeletedText;

	public string DeletedText => TrashEntry is null ? string.Empty : FormatHelpers.FormatDate(TrashEntry.DeletionDate);

	public bool IsImage => TypeInfo.Kind == FileKind.Image;

	[ObservableProperty]
	public partial bool IsCut { get; set; }

	[ObservableProperty]
	public partial Bitmap? Thumbnail { get; set; }

	public bool HasThumbnail => Thumbnail is not null;

	partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));

	/// <summary>Name shown in the UI. Extensions can be hidden through settings.</summary>
	public string DisplayName => IsDirectory || SettingsService.Current.ShowFileExtensions || TrashEntry is not null
		? Name
		: (Path.GetFileNameWithoutExtension(Name) is { Length: > 0 } n ? n : Name);

	public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));

	public static FileItem FromPath(string path)
	{
		var info = new FileInfo(path);
		if ((info.Attributes & FileAttributes.Directory) != 0 || Directory.Exists(path))
			return FromInfo(new DirectoryInfo(path));
		return FromInfo(info);
	}

	public static FileItem FromInfo(FileSystemInfo info, TrashEntry? trash = null)
	{
		var isDir = info is DirectoryInfo;
		long size = 0;
		bool isLink = info.LinkTarget is not null;

		if (!isDir && info is FileInfo fi)
		{
			try
			{
				size = fi.Exists ? fi.Length : 0;
			}
			catch
			{
				size = 0;
			}
		}

		var name = trash is not null ? Path.GetFileName(trash.OriginalPath) : info.Name;
		if (string.IsNullOrEmpty(name))
			name = info.FullName;

		return new FileItem
		{
			Name = name,
			FullPath = info.FullName,
			IsDirectory = isDir,
			IsHidden = info.Name.StartsWith('.'),
			IsSymlink = isLink,
			Size = size,
			Modified = SafeTime(() => info.LastWriteTime),
			Created = SafeTime(() => info.CreationTime),
			TrashEntry = trash,
		};
	}

	private static DateTime SafeTime(Func<DateTime> getter)
	{
		try
		{
			return getter();
		}
		catch
		{
			return DateTime.MinValue;
		}
	}

	public override string ToString() => FullPath;
}
