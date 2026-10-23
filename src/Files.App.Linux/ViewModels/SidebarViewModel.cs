// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.App.Linux.Models;
using Files.App.Linux.Services;
using FluentIcons.Common;

namespace Files.App.Linux.ViewModels;

public sealed partial class SidebarViewModel : ObservableObject
{
	private static readonly IBrush AccentFolder = new SolidColorBrush(Color.Parse("#FFC83D"));
	private static readonly IBrush DesktopBrush = new SolidColorBrush(Color.Parse("#3A96DD"));
	private static readonly IBrush DownloadsBrush = new SolidColorBrush(Color.Parse("#2EA043"));
	private static readonly IBrush DocumentsBrush = new SolidColorBrush(Color.Parse("#5B8DEF"));
	private static readonly IBrush PicturesBrush = new SolidColorBrush(Color.Parse("#2E9BD6"));
	private static readonly IBrush MusicBrush = new SolidColorBrush(Color.Parse("#E3008C"));
	private static readonly IBrush VideosBrush = new SolidColorBrush(Color.Parse("#8764B8"));
	private static readonly IBrush DriveBrush = new SolidColorBrush(Color.Parse("#7A7A7A"));
	private static readonly IBrush HomeBrush = new SolidColorBrush(Color.Parse("#E8A33D"));

	private string _mountSignature = string.Empty;
	private readonly DispatcherTimer? _timer;

	public ObservableCollection<SidebarItem> Items { get; } = [];

	public IReadOnlyList<DriveEntry> Drives { get; private set; } = [];

	public event EventHandler? DrivesChanged;

	[ObservableProperty]
	public partial SidebarItem? SelectedItem { get; set; }

	public SidebarViewModel(bool watchMounts = true)
	{
		Rebuild();
		if (watchMounts)
		{
			_timer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => CheckMounts());
			_timer.Start();
		}
	}

	public static List<string> DefaultPinned() =>
	[
		XdgPaths.GetUserDir("DESKTOP"),
		XdgPaths.GetUserDir("DOWNLOAD"),
		XdgPaths.GetUserDir("DOCUMENTS"),
		XdgPaths.GetUserDir("PICTURES"),
		XdgPaths.GetUserDir("MUSIC"),
		XdgPaths.GetUserDir("VIDEOS"),
		TrashService.TrashUri,
	];

	public static List<string> PinnedPaths => SettingsService.Current.PinnedFolders ?? DefaultPinned();

	public static (string Name, Symbol Icon, IBrush Brush) Describe(string path)
	{
		if (path == TrashService.TrashUri)
			return (Loc.Get("Trash", "Trash"), Symbol.Delete, DriveBrush);
		if (path == XdgPaths.Home)
			return (Path.GetFileName(path), Symbol.Home, HomeBrush);
		if (path == XdgPaths.GetUserDir("DESKTOP"))
			return (Loc.Get("Desktop", "Desktop"), Symbol.Desktop, DesktopBrush);
		if (path == XdgPaths.GetUserDir("DOWNLOAD"))
			return (Loc.Get("Downloads", "Downloads"), Symbol.ArrowDownload, DownloadsBrush);
		if (path == XdgPaths.GetUserDir("DOCUMENTS"))
			return (Loc.Get("Documents", "Documents"), Symbol.Document, DocumentsBrush);
		if (path == XdgPaths.GetUserDir("PICTURES"))
			return (Loc.Get("Pictures", "Pictures"), Symbol.Image, PicturesBrush);
		if (path == XdgPaths.GetUserDir("MUSIC"))
			return (Loc.Get("Music", "Music"), Symbol.MusicNote2, MusicBrush);
		if (path == XdgPaths.GetUserDir("VIDEOS"))
			return (Loc.Get("Videos", "Videos"), Symbol.Video, VideosBrush);
		if (path == "/")
			return (Loc.Get("FileSystem", "File System"), Symbol.HardDrive, DriveBrush);
		return (Path.GetFileName(path.TrimEnd('/')) is { Length: > 0 } n ? n : path, Symbol.Folder, AccentFolder);
	}

	public void Rebuild()
	{
		var selectedPath = SelectedItem?.Path;
		var selectedKind = SelectedItem?.Kind;
		var collapsed = Items.Where(i => i.IsHeader && !i.IsExpanded).Select(i => i.Section).ToHashSet();

		Items.Clear();
		Items.Add(new SidebarItem { Text = Loc.Get("Home", "Home"), Kind = SidebarItemKind.Home, Icon = Symbol.Home, IconBrush = HomeBrush });

		Items.Add(new SidebarItem { Text = Loc.Get("Pinned", "Pinned"), Kind = SidebarItemKind.Header, Section = SidebarSection.Pinned, IsExpanded = !collapsed.Contains(SidebarSection.Pinned) });
		foreach (var path in PinnedPaths)
		{
			if (path != TrashService.TrashUri && !Directory.Exists(path))
				continue;
			var (name, icon, brush) = Describe(path);
			Items.Add(new SidebarItem
			{
				Text = name,
				Path = path,
				Icon = icon,
				IconBrush = brush,
				Kind = path == TrashService.TrashUri ? SidebarItemKind.Trash : SidebarItemKind.Pinned,
				Section = SidebarSection.Pinned,
			});
		}

		Items.Add(new SidebarItem { Text = Loc.Get("Drives", "Drives"), Kind = SidebarItemKind.Header, Section = SidebarSection.Drives, IsExpanded = !collapsed.Contains(SidebarSection.Drives) });
		Drives = MountService.GetDrives();
		_mountSignature = string.Join("|", Drives.Select(d => d.MountPoint));
		foreach (var d in Drives)
		{
			Items.Add(new SidebarItem
			{
				Text = d.Name,
				Path = d.MountPoint,
				Icon = d.IsRoot ? Symbol.HardDrive : (d.IsRemovable ? Symbol.UsbStick : Symbol.Storage),
				IconBrush = DriveBrush,
				Kind = SidebarItemKind.Drive,
				Section = SidebarSection.Drives,
				Drive = new DriveEntryInfo(d.MountPoint, d.Device, d.FsType, d.TotalBytes, d.FreeBytes, d.IsRemovable, d.SpaceText, d.UsedPercent, d.IsLowOnSpace),
			});
		}

		UpdateVisibility();
		SelectedItem = Items.FirstOrDefault(i => i.Kind == selectedKind && i.Path == selectedPath);
		DrivesChanged?.Invoke(this, EventArgs.Empty);
	}

	public void ToggleSection(SidebarItem header)
	{
		header.IsExpanded = !header.IsExpanded;
		UpdateVisibility();
	}

	private void UpdateVisibility()
	{
		var expanded = Items.Where(i => i.IsHeader).ToDictionary(i => i.Section, i => i.IsExpanded);
		foreach (var item in Items)
		{
			if (!item.IsHeader && item.Section != SidebarSection.None)
				item.IsVisible = expanded.GetValueOrDefault(item.Section, true);
		}
	}

	private void CheckMounts()
	{
		var sig = string.Join("|", MountService.GetDrives().Select(d => d.MountPoint));
		if (sig != _mountSignature)
			Rebuild();
	}

	/// <summary>Highlights the sidebar entry that matches the current location.</summary>
	public void SyncSelection(string? currentPath)
	{
		SidebarItem? match;
		if (currentPath is null)
			match = Items.FirstOrDefault(i => i.Kind == SidebarItemKind.Home);
		else
			match = Items.FirstOrDefault(i => !i.IsHeader && i.Path == currentPath);
		SelectedItem = match;
	}

	public bool IsPinned(string path) => PinnedPaths.Contains(path);

	public void Pin(string path)
	{
		var list = PinnedPaths.ToList();
		if (list.Contains(path))
			return;
		list.Add(path);
		SettingsService.Current.PinnedFolders = list;
		SettingsService.Save();
		Rebuild();
	}

	public void Unpin(string path)
	{
		var list = PinnedPaths.ToList();
		if (!list.Remove(path))
			return;
		SettingsService.Current.PinnedFolders = list;
		SettingsService.Save();
		Rebuild();
	}

	public void MovePinned(string path, int delta)
	{
		var list = PinnedPaths.ToList();
		var index = list.IndexOf(path);
		var target = index + delta;
		if (index < 0 || target < 0 || target >= list.Count)
			return;
		list.RemoveAt(index);
		list.Insert(target, path);
		SettingsService.Current.PinnedFolders = list;
		SettingsService.Save();
		Rebuild();
	}
}
