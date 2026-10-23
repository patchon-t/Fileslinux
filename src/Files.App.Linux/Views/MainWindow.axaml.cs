// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Files.App.Linux.Models;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using FluentIcons.Common;
using static Files.App.Linux.Helpers.MenuBuilder;

namespace Files.App.Linux.Views;

public partial class MainWindow : Window
{
	private MainWindowViewModel? _vm;

	public MainWindow()
	{
		InitializeComponent();

		var s = SettingsService.Current;
		Width = s.WindowWidth;
		Height = s.WindowHeight;
		if (s.WindowMaximized)
			WindowState = WindowState.Maximized;
		ContentGrid.ColumnDefinitions[0].Width = new GridLength(s.SidebarWidth);

		AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
		AddHandler(TextInputEvent, OnWindowTextInput, RoutingStrategies.Bubble);
		AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

		BreadcrumbHost.PointerPressed += (_, e) =>
		{
			if (e.Source == BreadcrumbHost || e.Source is ScrollViewer || e.Source is ItemsControl || e.Source is StackPanel)
				BeginEditPath();
		};
		PathBox.KeyDown += OnPathBoxKeyDown;
		PathBox.LostFocus += (_, _) => EndEditPath();
		SearchBox.KeyDown += OnSearchBoxKeyDown;

		SidebarList.AddHandler(PointerReleasedEvent, OnSidebarPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
		SidebarList.ContextRequested += OnSidebarContextRequested;
		SidebarList.KeyDown += OnSidebarKeyDown;
		DragDrop.SetAllowDrop(SidebarList, true);
		SidebarList.AddHandler(DragDrop.DragOverEvent, OnSidebarDragOver);
		SidebarList.AddHandler(DragDrop.DropEvent, OnSidebarDrop);
		SidebarSplitter.DragCompleted += (_, _) =>
		{
			SettingsService.Current.SidebarWidth = ContentGrid.ColumnDefinitions[0].ActualWidth;
			SettingsService.Save();
		};

		BackButton.ContextRequested += (_, e) => ShowHistoryMenu(BackButton, back: true, e);
		ForwardButton.ContextRequested += (_, e) => ShowHistoryMenu(ForwardButton, back: false, e);

		Opened += (_, _) =>
		{
			ClipboardService.SystemClipboard = Clipboard;
			Shell.FocusActiveList();
		};
		Closing += (_, _) => SaveWindowState();
		Closed += (_, _) => _vm?.Detach();
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);
		if (_vm is not null)
			_vm.PropertyChanged -= OnVmPropertyChanged;
		_vm = DataContext as MainWindowViewModel;
		if (_vm is null)
			return;
		_vm.Dialogs = new OverlayDialogService(this);
		_vm.CloseRequested += (_, _) => Close();
		_vm.PropertyChanged += OnVmPropertyChanged;
		UpdateSidebarColumn();
	}

	private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarOpen))
			UpdateSidebarColumn();
		else if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab))
		{
			SearchBox.Text = _vm?.SelectedTab?.IsSearchResults == true ? _vm.SelectedTab.SearchQuery : string.Empty;
			EndEditPath();
			Dispatcher.UIThread.Post(() =>
			{
				if (TabList.SelectedItem is { } sel)
					TabList.ScrollIntoView(sel);
				Shell.FocusActiveList();
			}, DispatcherPriority.Background);
		}
	}

	private void UpdateSidebarColumn()
	{
		if (_vm is null)
			return;
		ContentGrid.ColumnDefinitions[0].Width = _vm.IsSidebarOpen ? new GridLength(Math.Max(160, SettingsService.Current.SidebarWidth)) : new GridLength(0);
	}

	private void SaveWindowState()
	{
		var s = SettingsService.Current;
		s.WindowMaximized = WindowState == WindowState.Maximized;
		if (WindowState == WindowState.Normal)
		{
			s.WindowWidth = Bounds.Width;
			s.WindowHeight = Bounds.Height;
		}
		_vm?.SaveSession();
		SettingsService.Save();
	}

	private ShellTabViewModel? Tab => _vm?.SelectedTab;

	// ---------------- Address bar ----------------

	private void BeginEditPath()
	{
		if (Tab is null)
			return;
		PathBox.Text = Tab.IsHome ? string.Empty : Tab.IsTrash ? TrashService.TrashUri : Tab.CurrentPath;
		BreadcrumbHost.IsVisible = false;
		PathBox.IsVisible = true;
		PathBox.Focus();
		PathBox.SelectAll();
	}

	private void EndEditPath()
	{
		PathBox.IsVisible = false;
		BreadcrumbHost.IsVisible = true;
		Dispatcher.UIThread.Post(() => BreadcrumbScroller.ScrollToEnd(), DispatcherPriority.Background);
	}

	private void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			var text = PathBox.Text ?? string.Empty;
			EndEditPath();
			Tab?.NavigateTo(text.Length == 0 ? null : text);
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			EndEditPath();
			Shell.FocusActiveList();
			e.Handled = true;
		}
		else if (e.Key == Key.Tab)
		{
			CompletePath();
			e.Handled = true;
		}
	}

	/// <summary>Tab-completes a folder name in the path box.</summary>
	private void CompletePath()
	{
		var text = PathBox.Text ?? string.Empty;
		var expanded = text.StartsWith('~') ? XdgPaths.Home + text[1..] : text;
		var dir = Path.GetDirectoryName(expanded);
		var prefix = Path.GetFileName(expanded);
		if (dir is null || !Directory.Exists(dir))
			return;
		try
		{
			var matches = Directory.EnumerateDirectories(dir, prefix + "*").Order(StringComparer.CurrentCultureIgnoreCase).ToList();
			if (matches.Count == 0)
				return;
			PathBox.Text = matches[0] + "/";
			PathBox.CaretIndex = PathBox.Text.Length;
		}
		catch
		{
			// Ignore unreadable folders.
		}
	}

	private void OnBreadcrumbClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Control { DataContext: BreadcrumbItem crumb } && Tab is { } tab)
		{
			if (crumb.IsLast && !tab.IsSearchResults)
				BeginEditPath();
			else
				tab.NavigateTo(crumb.Path);
		}
	}

	private void OnBreadcrumbChevronClick(object? sender, RoutedEventArgs e)
	{
		if (sender is not Control { DataContext: BreadcrumbItem { Path: { } path } } control)
			return;
		var menu = new ContextMenu();
		try
		{
			var showHidden = SettingsService.Current.ShowHiddenItems;
			var dirs = Directory.EnumerateDirectories(path)
				.Where(d => showHidden || !Path.GetFileName(d).StartsWith('.'))
				.Order(StringComparer.CurrentCultureIgnoreCase)
				.Take(200)
				.ToList();
			foreach (var d in dirs)
				menu.Items.Add(Action(Path.GetFileName(d), Symbol.Folder, () => Tab?.NavigateTo(d)));
			if (dirs.Count == 0)
				menu.Items.Add(new MenuItem { Header = Loc.Get("EmptyFolder.Text", "This folder is empty."), IsEnabled = false });
		}
		catch (Exception ex)
		{
			menu.Items.Add(new MenuItem { Header = ex.Message, IsEnabled = false });
		}
		menu.Open(control);
	}

	private void ShowHistoryMenu(Control anchor, bool back, ContextRequestedEventArgs e)
	{
		e.Handled = true;
		if (Tab is not { } tab)
			return;
		var history = back ? tab.BackHistory : tab.ForwardHistory;
		var menu = new ContextMenu();
		int steps = 0;
		foreach (var entry in history.Take(15))
		{
			int count = ++steps;
			var name = entry is null ? Loc.Get("Home", "Home") : SidebarViewModel.Describe(entry).Name;
			menu.Items.Add(Action(name, entry is null ? Symbol.Home : Symbol.Folder, () =>
			{
				for (int i = 0; i < count; i++)
				{
					if (back)
						tab.GoBackCommand.Execute(null);
					else
						tab.GoForwardCommand.Execute(null);
				}
			}));
		}
		if (menu.Items.Count > 0)
			menu.Open(anchor);
	}

	private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			_ = Tab?.SearchAsync(SearchBox.Text);
			Shell.FocusActiveList();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			SearchBox.Text = string.Empty;
			if (Tab?.IsSearchResults == true)
				_ = Tab.SearchAsync(null);
			Shell.FocusActiveList();
			e.Handled = true;
		}
	}

	// ---------------- Sidebar ----------------

	private static SidebarItem? SidebarItemFromSource(object? source)
	{
		var visual = source as Visual;
		while (visual is not null)
		{
			if (visual is ListBoxItem { DataContext: SidebarItem item })
				return item;
			visual = visual.GetVisualParent();
		}
		return null;
	}

	private void OnSidebarPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (_vm is null || SidebarItemFromSource(e.Source) is not { } item)
			return;
		if (e.InitialPressMouseButton == MouseButton.Left)
		{
			_vm.NavigateSidebarItem(item, newTab: e.KeyModifiers.HasFlag(KeyModifiers.Control));
			if (item.IsHeader)
				Dispatcher.UIThread.Post(() => _vm.Sidebar.SyncSelection(Tab?.CurrentPath));
		}
		else if (e.InitialPressMouseButton == MouseButton.Middle && item.IsNavigable)
		{
			_vm.NavigateSidebarItem(item, newTab: true);
		}
	}

	private void OnSidebarKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key is Key.Enter or Key.Space && SidebarList.SelectedItem is SidebarItem selected)
		{
			_vm?.NavigateSidebarItem(selected, newTab: e.KeyModifiers.HasFlag(KeyModifiers.Control));
			e.Handled = true;
		}
	}

	private void OnSidebarContextRequested(object? sender, ContextRequestedEventArgs e)
	{
		e.Handled = true;
		if (_vm is null || SidebarItemFromSource(e.Source) is not { IsNavigable: true } item)
			return;
		var vm = _vm;
		var path = item.Kind == SidebarItemKind.Home ? null : item.Path;
		var menu = new ContextMenu();
		menu.Items.Add(Action(Loc.Get("OpenInNewTab", "Open in new tab"), Symbol.TabAdd, () => vm.OpenTab(path)));
		if (item.Kind == SidebarItemKind.Trash)
		{
			menu.Items.Add(Separator());
			menu.Items.Add(Action(Loc.Get("EmptyTrash", "Empty trash"), Symbol.DeleteDismiss, () =>
			{
				var tab = vm.SelectedTab ?? vm.OpenTab(null);
				tab.EmptyTrashCommand.Execute(null);
			}));
		}
		if (path is not null && path != TrashService.TrashUri)
		{
			menu.Items.Add(Action(Loc.Get("OpenInTerminal", "Open in Terminal"), Symbol.WindowConsole, () =>
			{
				try
				{
					LauncherService.OpenInTerminal(path);
				}
				catch (Exception ex)
				{
					_ = vm.Dialogs.ShowErrorAsync(Loc.Get("OpenInTerminal", "Open in Terminal"), ex.Message);
				}
			}));
		}
		if (item.Section == SidebarSection.Pinned && path is not null)
		{
			menu.Items.Add(Separator());
			menu.Items.Add(Action(Loc.Get("MoveItemUp", "Move up"), Symbol.ArrowUp, () => { vm.Sidebar.MovePinned(path, -1); vm.OnPinnedChanged(); }));
			menu.Items.Add(Action(Loc.Get("MoveItemDown", "Move down"), Symbol.ArrowDown, () => { vm.Sidebar.MovePinned(path, 1); vm.OnPinnedChanged(); }));
			menu.Items.Add(Action(Loc.Get("UnpinFolderFromSidebar", "Unpin from Sidebar"), Symbol.PinOff, () => { vm.Sidebar.Unpin(path); vm.OnPinnedChanged(); }));
		}
		else if (item.Kind == SidebarItemKind.Drive && path is not null)
		{
			if (!vm.Sidebar.IsPinned(path))
				menu.Items.Add(Action(Loc.Get("PinFolderToSidebar", "Pin to Sidebar"), Symbol.Pin, () => { vm.Sidebar.Pin(path); vm.OnPinnedChanged(); }));
			if (item.CanEject)
			{
				menu.Items.Add(Action(Loc.Get("Eject", "Eject"), Symbol.ArrowEject, () =>
				{
					if (!LauncherService.Eject(path))
						_ = vm.Dialogs.ShowErrorAsync(Loc.Get("Eject", "Eject"), $"Couldn't unmount \"{path}\". It may be in use.");
					vm.Sidebar.Rebuild();
				}));
			}
		}
		if (path is not null && path != TrashService.TrashUri)
		{
			menu.Items.Add(Separator());
			menu.Items.Add(Action(Loc.Get("Properties", "Properties"), Symbol.Info, () => vm.Dialogs.ShowProperties([path])));
		}
		menu.Open(SidebarList);
	}

	private void OnSidebarDragOver(object? sender, DragEventArgs e)
	{
		var item = SidebarItemFromSource(e.Source);
		var paths = ShellView.GetDroppedPaths(e.Data);
		if (item is null || item.Path is null || paths.Count == 0)
		{
			e.DragEffects = DragDropEffects.None;
			return;
		}
		e.DragEffects = item.Kind == SidebarItemKind.Trash ? DragDropEffects.Move : (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move);
	}

	private async void OnSidebarDrop(object? sender, DragEventArgs e)
	{
		var item = SidebarItemFromSource(e.Source);
		var paths = ShellView.GetDroppedPaths(e.Data);
		if (_vm is null || item?.Path is null || paths.Count == 0)
			return;
		e.Handled = true;
		if (item.Kind == SidebarItemKind.Trash)
		{
			var errors = new List<string>();
			await Task.Run(() =>
			{
				foreach (var p in paths)
				{
					try
					{
						TrashService.Default.Trash(p);
					}
					catch (Exception ex)
					{
						errors.Add($"{Path.GetFileName(p)}: {ex.Message}");
					}
				}
			});
			if (errors.Count > 0)
				await _vm.Dialogs.ShowErrorAsync(Loc.Get("Delete", "Delete"), string.Join("\n", errors));
			_vm.RefreshAll();
			return;
		}
		if (Tab is { } tab)
			await tab.DropAsync(paths, item.Path, e.KeyModifiers.HasFlag(KeyModifiers.Control));
	}

	// ---------------- Keyboard ----------------

	private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		var props = e.GetCurrentPoint(this).Properties;
		if (props.IsXButton1Pressed)
		{
			Tab?.GoBackCommand.Execute(null);
			e.Handled = true;
		}
		else if (props.IsXButton2Pressed)
		{
			Tab?.GoForwardCommand.Execute(null);
			e.Handled = true;
		}
	}

	private bool IsTextInputFocused => FocusManager?.GetFocusedElement() is TextBox;

	/// <summary>File shortcuts (Enter, Delete, Ctrl+C...) apply unless the user is typing, or a button, menu or the sidebar has focus.</summary>
	private bool CanUseFileShortcuts()
	{
		var focused = FocusManager?.GetFocusedElement() as Control;
		if (focused is TextBox or Button or MenuItem or ComboBox)
			return false;
		return focused is null || (focused != SidebarList && focused.FindAncestorOfType<ListBox>() != SidebarList);
	}

	private void OnWindowTextInput(object? sender, TextInputEventArgs e)
	{
		if (DialogLayer.IsVisible || IsTextInputFocused || string.IsNullOrEmpty(e.Text))
			return;
		if (Shell.HandleTypeAhead(e.Text))
			e.Handled = true;
	}

	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (DialogLayer.IsVisible)
		{
			(_vm?.Dialogs as OverlayDialogService)?.HandleKey(e);
			return;
		}
		if (_vm is null)
			return;
		var tab = Tab;
		var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
		var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
		var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
		bool handled = true;

		// Shortcuts that work everywhere.
		switch (e.Key)
		{
			case Key.T when ctrl && shift:
				_vm.ReopenClosedTabCommand.Execute(null);
				break;
			case Key.T when ctrl && alt:
				tab?.OpenInTerminalCommand.Execute(null);
				break;
			case Key.T when ctrl:
				_vm.OpenTab(null);
				break;
			case Key.W when ctrl:
			case Key.F4 when ctrl:
				_vm.CloseTab(null);
				break;
			case Key.Tab when ctrl && shift:
				_vm.PreviousTabCommand.Execute(null);
				break;
			case Key.Tab when ctrl:
				_vm.NextTabCommand.Execute(null);
				break;
			case Key.PageDown when ctrl:
				_vm.NextTabCommand.Execute(null);
				break;
			case Key.PageUp when ctrl:
				_vm.PreviousTabCommand.Execute(null);
				break;
			case >= Key.D1 and <= Key.D5 when ctrl && shift:
				tab?.SetLayoutCommand.Execute((LayoutMode)(e.Key - Key.D1));
				break;
			case >= Key.D1 and <= Key.D9 when ctrl:
				_vm.SelectTabByNumber(e.Key - Key.D0);
				break;
			case Key.L when ctrl:
			case Key.D when alt:
			case Key.F4 when !alt:
				BeginEditPath();
				break;
			case Key.F when ctrl:
			case Key.F3:
				SearchBox.Focus();
				SearchBox.SelectAll();
				break;
			case Key.Left when alt:
				tab?.GoBackCommand.Execute(null);
				break;
			case Key.Right when alt:
				tab?.GoForwardCommand.Execute(null);
				break;
			case Key.Up when alt:
				tab?.GoUpCommand.Execute(null);
				break;
			case Key.Home when alt:
				tab?.NavigateTo(null);
				break;
			case Key.F5:
			case Key.R when ctrl:
				tab?.RefreshCommand.Execute(null);
				break;
			case Key.H when ctrl:
				_vm.ToggleHiddenItemsCommand.Execute(null);
				break;
			case Key.B when ctrl && shift:
				_vm.ToggleSidebarCommand.Execute(null);
				break;
			case Key.P when alt:
			case Key.I when ctrl && shift:
				_vm.ToggleInfoPaneCommand.Execute(null);
				break;
			case Key.N when ctrl && shift:
				tab?.NewFolderCommand.Execute(null);
				break;
			case Key.N when ctrl:
				OpenNewWindow();
				break;
			case Key.OemComma when ctrl:
				_vm.OpenSettingsCommand.Execute(null);
				break;
			default:
				handled = false;
				break;
		}

		if (handled || tab is null || !CanUseFileShortcuts())
		{
			e.Handled = handled;
			return;
		}

		// File shortcuts (only when not typing).
		handled = true;
		switch (e.Key)
		{
			case Key.C when ctrl && shift:
				tab.CopyPathCommand.Execute(null);
				break;
			case Key.C when ctrl:
				tab.CopyCommand.Execute(null);
				break;
			case Key.X when ctrl:
				tab.CutCommand.Execute(null);
				break;
			case Key.V when ctrl:
				tab.PasteCommand.Execute(null);
				break;
			case Key.A when ctrl:
				tab.SelectAllCommand.Execute(null);
				break;
			case Key.Delete when shift:
				tab.DeletePermanentlyCommand.Execute(null);
				break;
			case Key.Delete:
				tab.DeleteCommand.Execute(null);
				break;
			case Key.F2:
				tab.RenameCommand.Execute(null);
				break;
			case Key.Enter when alt:
				tab.ShowPropertiesCommand.Execute(null);
				break;
			case Key.Enter when ctrl:
				tab.OpenInNewTabCommand.Execute(null);
				break;
			case Key.Enter:
				tab.OpenSelectedCommand.Execute(null);
				break;
			case Key.Back:
				tab.GoBackCommand.Execute(null);
				break;
			case Key.Escape:
				tab.ClearSelectionCommand.Execute(null);
				break;
			case Key.Apps:
			case Key.F10 when shift:
				Shell.OpenContextMenuForSelection();
				break;
			default:
				handled = false;
				break;
		}
		e.Handled = handled;
	}

	private void OpenNewWindow()
	{
		var vm = new MainWindowViewModel();
		var window = new MainWindow { DataContext = vm };
		vm.Initialize(Tab?.CurrentPath is { } p ? [p] : []);
		window.Show();
	}
}
