// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Files.App.Linux.Controls;
using Files.App.Linux.Models;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Files.App.Linux.Views;

public partial class HomeView : UserControl
{
	/// <summary>Builds the folder (or trash) background icon for a Quick access card.</summary>
	public static readonly IValueConverter FolderIconConverter = new FuncValueConverter<QuickAccessCard?, Control?>(card =>
		card?.Path == TrashService.TrashUri
			? new SymbolIcon { Symbol = Symbol.Delete, FontSize = 44, Foreground = card.Brush }
			: ItemIcon.BuildFolder(48));

	public HomeView()
	{
		InitializeComponent();
	}

	private ShellTabViewModel? Tab => DataContext as ShellTabViewModel;

	private void OnQuickAccessClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: QuickAccessCard card })
			Tab?.NavigateTo(card.Path);
	}

	private void OnQuickAccessOpenInNewTab(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: QuickAccessCard card })
			Tab?.Main.OpenTab(card.Path);
	}

	private void OnQuickAccessUnpin(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: QuickAccessCard card } && Tab is { } tab)
		{
			tab.Main.Sidebar.Unpin(card.Path);
			tab.Main.OnPinnedChanged();
		}
	}

	private void OnDriveClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: DriveCard drive })
			Tab?.NavigateTo(drive.Path);
	}

	private void OnRecentClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: FileItem item })
			Tab?.OpenItem(item);
	}

	private void OnRecentOpenLocation(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: FileItem item })
			Tab?.OpenFileLocationCommand.Execute(item);
	}
}
