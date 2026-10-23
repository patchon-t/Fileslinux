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

public sealed record QuickAccessCard(string Name, string Path, Symbol Icon, IBrush Brush)
{
	/// <summary>Folders show the folder artwork with a small badge; the trash shows its own icon only.</summary>
	public bool ShowBadge => Path != TrashService.TrashUri && Icon != Symbol.Folder;
}

public sealed record DriveCard(string Name, string Path, Symbol Icon, string SpaceText, double UsedPercent, bool IsLowOnSpace, string FsType);

/// <summary>Home page widgets: Quick access, Drives, Recent files (as on Windows).</summary>
public sealed partial class HomeViewModel : ObservableObject
{
	public ObservableCollection<QuickAccessCard> QuickAccess { get; } = [];

	public ObservableCollection<DriveCard> Drives { get; } = [];

	public ObservableCollection<FileItem> RecentFiles { get; } = [];

	[ObservableProperty]
	public partial bool HasRecentFiles { get; set; }

	public void Load(SidebarViewModel sidebar)
	{
		QuickAccess.Clear();
		foreach (var path in SidebarViewModel.PinnedPaths)
		{
			if (path != TrashService.TrashUri && !Directory.Exists(path))
				continue;
			var (name, icon, brush) = SidebarViewModel.Describe(path);
			QuickAccess.Add(new QuickAccessCard(name, path, icon, brush));
		}

		Drives.Clear();
		foreach (var d in sidebar.Drives)
		{
			var icon = d.IsRoot ? Symbol.HardDrive : (d.IsRemovable ? Symbol.UsbStick : Symbol.Storage);
			Drives.Add(new DriveCard(d.Name, d.MountPoint, icon, d.SpaceText, d.UsedPercent, d.IsLowOnSpace, d.FsType));
		}

		_ = Task.Run(() =>
		{
			var recent = FileSystemService.GetRecentFiles();
			Dispatcher.UIThread.Post(() =>
			{
				RecentFiles.Clear();
				foreach (var r in recent)
					RecentFiles.Add(r);
				HasRecentFiles = RecentFiles.Count > 0;
			});
		});
	}
}
