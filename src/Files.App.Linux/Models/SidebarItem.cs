// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentIcons.Common;

namespace Files.App.Linux.Models;

public enum SidebarItemKind
{
	Home,
	Header,
	Pinned,
	Drive,
	Trash,
}

public enum SidebarSection
{
	None,
	Pinned,
	Drives,
}

public sealed partial class SidebarItem : ObservableObject
{
	public required string Text { get; init; }

	/// <summary>Navigation target. Null for Home and headers; "trash://" for the trash.</summary>
	public string? Path { get; init; }

	public Symbol Icon { get; init; } = Symbol.Folder;

	public IBrush? IconBrush { get; init; }

	public SidebarItemKind Kind { get; init; }

	public SidebarSection Section { get; init; }

	public DriveEntryInfo? Drive { get; init; }

	/// <summary>Place key for icon themes and packs (home, desktop, ..., trash, drive, folder).</summary>
	public string? PlaceKey { get; init; }

	/// <summary>Icon from the chosen icon theme or pack; null shows the built-in Fluent icon.</summary>
	public Avalonia.Media.IImage? CustomIcon => PlaceKey is null ? null : Services.Icons.IconService.GetPlaceIcon(PlaceKey, 32);

	public bool HasCustomIcon => CustomIcon is not null;

	/// <summary>Drives show the Windows-style drive picture (unless an icon theme or pack is used).</summary>
	public bool ShowsDriveArt => Kind == SidebarItemKind.Drive && !HasCustomIcon;

	/// <summary>Everything else shows its Fluent icon.</summary>
	public bool ShowsSymbol => !HasCustomIcon && Kind != SidebarItemKind.Drive;

	public bool IsHeader => Kind == SidebarItemKind.Header;

	public bool IsNavigable => Kind != SidebarItemKind.Header;

	public bool IsPinned => Kind is SidebarItemKind.Pinned || (Kind is SidebarItemKind.Trash && Section == SidebarSection.Pinned);

	public bool CanEject => Drive?.IsRemovable == true;

	[ObservableProperty]
	public partial bool IsExpanded { get; set; } = true;

	[ObservableProperty]
	public partial bool IsVisible { get; set; } = true;

	public string ToolTip => Drive is { } d && !string.IsNullOrEmpty(d.SpaceText) ? $"{Text}\n{d.SpaceText}" : (Path ?? Text);
}

/// <summary>Drive data used by the sidebar and Home page.</summary>
public sealed record DriveEntryInfo(string MountPoint, string Device, string FsType, long TotalBytes, long FreeBytes, bool IsRemovable, string SpaceText, double UsedPercent, bool IsLowOnSpace);
