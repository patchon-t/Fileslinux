// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Files.App.Linux.Controls;
using Files.App.Linux.Helpers;
using Files.App.Linux.Models;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using static Files.App.Linux.Helpers.MenuBuilder;

namespace Files.App.Linux.Views;

public partial class ShellView : UserControl
{
	internal const string InternalDragFormat = "application/x-files-app-paths";

	private ShellTabViewModel? _tab;
	private bool _syncingSelection;
	private Point? _dragStart;
	private string _typeAhead = string.Empty;
	private long _typeAheadTime;

	public ShellView()
	{
		InitializeComponent();

		foreach (var list in AllLists)
		{
			list.SelectionChanged += OnListSelectionChanged;
			list.DoubleTapped += OnListDoubleTapped;
			list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
			list.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
			list.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel);
			list.ContextRequested += OnListContextRequested;
			DragDrop.SetAllowDrop(list, true);
			list.AddHandler(DragDrop.DragOverEvent, OnDragOver);
			list.AddHandler(DragDrop.DropEvent, OnDrop);
		}

		DetailsList.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => SyncHeaderScroll());
		DetailsColumns.Changed += (_, _) => BuildHeader();
		var sortFlyout = (MenuFlyout)SortButton.Flyout!;
		var layoutFlyout = (MenuFlyout)LayoutButton.Flyout!;
		var moreFlyout = (MenuFlyout)MoreButton.Flyout!;
		sortFlyout.Opening += (_, _) => BuildSortMenu(sortFlyout.Items);
		layoutFlyout.Opening += (_, _) => BuildLayoutMenu(layoutFlyout.Items);
		moreFlyout.Opening += (_, _) => BuildMoreMenu(moreFlyout.Items);
		InfoSplitter.DragCompleted += (_, _) =>
		{
			SettingsService.Current.InfoPaneWidth = InfoPane.Bounds.Width;
			SettingsService.Save();
		};
		InfoPane.Width = SettingsService.Current.InfoPaneWidth;
		BuildHeader();
	}

	private IEnumerable<ListBox> AllLists => [DetailsList, ListList, GridList];

	public ListBox ActiveList => _tab?.Layout switch
	{
		LayoutMode.Details => DetailsList,
		LayoutMode.List => ListList,
		_ => GridList,
	};

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);
		if (_tab is not null)
		{
			_tab.SelectionRequested -= OnSelectionRequested;
			_tab.Navigated -= OnNavigated;
			_tab.PropertyChanged -= OnTabPropertyChanged;
		}
		_tab = DataContext as ShellTabViewModel;
		if (_tab is not null)
		{
			_tab.SelectionRequested += OnSelectionRequested;
			_tab.Navigated += OnNavigated;
			_tab.PropertyChanged += OnTabPropertyChanged;
			DetailsColumns.SetShowLocation(_tab.ShowLocationColumn);
			BuildHeader();
			Dispatcher.UIThread.Post(() => ApplySelection(_tab.SelectedItems), DispatcherPriority.Background);
		}
	}

	private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(ShellTabViewModel.ShowLocationColumn) or nameof(ShellTabViewModel.IsSearchResults) or nameof(ShellTabViewModel.CurrentPath))
		{
			DetailsColumns.SetShowLocation(_tab?.ShowLocationColumn == true);
			BuildHeader();
		}
		else if (e.PropertyName == nameof(ShellTabViewModel.Layout) && _tab is not null)
		{
			var selected = _tab.SelectedItems;
			Dispatcher.UIThread.Post(() =>
			{
				ApplySelection(selected);
				FocusActiveList();
			}, DispatcherPriority.Background);
		}
	}

	private void OnNavigated(object? sender, EventArgs e)
	{
		Dispatcher.UIThread.Post(() =>
		{
			var list = ActiveList;
			if (list.Scroll is ScrollViewer sv && _tab?.SelectedItems.Count == 0)
				sv.Offset = default;
			if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
				FocusActiveList();
		}, DispatcherPriority.Background);
	}

	public void FocusActiveList()
	{
		if (_tab is null || _tab.IsHome)
			return;
		var list = ActiveList;
		if (list.SelectedIndex >= 0 && list.ContainerFromIndex(list.SelectedIndex) is { } container)
			container.Focus();
		else
			list.Focus();
	}

	// ---------------- Selection ----------------

	private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_syncingSelection || _tab is null || sender != ActiveList)
			return;
		var list = (ListBox)sender;
		_tab.SetSelection(list.SelectedItems?.OfType<FileItem>().ToList() ?? []);
	}

	private void OnSelectionRequested(object? sender, IReadOnlyList<FileItem> items) => ApplySelection(items, scroll: true);

	private void ApplySelection(IReadOnlyList<FileItem> items, bool scroll = false)
	{
		if (_tab is null)
			return;
		var list = ActiveList;
		_syncingSelection = true;
		try
		{
			list.SelectedItems?.Clear();
			foreach (var i in items)
				list.SelectedItems?.Add(i);
		}
		finally
		{
			_syncingSelection = false;
		}
		_tab.SetSelection(items);
		if (scroll && items.Count > 0)
		{
			list.ScrollIntoView(items[0]);
			Dispatcher.UIThread.Post(() =>
			{
				if (list.ContainerFromItem(items[0]) is { } c)
					c.Focus();
			}, DispatcherPriority.Background);
		}
	}

	private void OnListDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (_tab is null)
			return;
		if (ItemFromSource(e.Source) is { } item)
			_tab.OpenItem(item);
	}

	private static FileItem? ItemFromSource(object? source)
	{
		var visual = source as Visual;
		while (visual is not null)
		{
			if (visual is ListBoxItem { DataContext: FileItem item })
				return item;
			if (visual is ListBox)
				return null;
			visual = visual.GetVisualParent();
		}
		return null;
	}

	private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not ListBox list)
			return;
		var point = e.GetCurrentPoint(list);

		// Mouse back/forward buttons.
		if (point.Properties.IsXButton1Pressed)
		{
			_tab?.GoBackCommand.Execute(null);
			e.Handled = true;
			return;
		}
		if (point.Properties.IsXButton2Pressed)
		{
			_tab?.GoForwardCommand.Execute(null);
			e.Handled = true;
			return;
		}

		var item = ItemFromSource(e.Source);
		if (point.Properties.IsLeftButtonPressed)
		{
			_dragStart = item is not null ? point.Position : null;
			if (item is null && e.KeyModifiers == KeyModifiers.None)
				ApplySelection([]); // Click on empty space clears the selection.
		}
		else if (point.Properties.IsMiddleButtonPressed && item is { IsDirectory: true })
		{
			_tab?.Main.OpenTab(item.FullPath, activate: false);
			e.Handled = true;
		}
		else if (point.Properties.IsRightButtonPressed)
		{
			// Right-click selects the item under the pointer unless it is already part of the selection.
			if (item is null)
				ApplySelection([]);
			else if (_tab is not null && !_tab.SelectedItems.Contains(item))
				ApplySelection([item]);
			e.Handled = true;
		}
	}

	private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e) => _dragStart = null;

	// ---------------- Drag and drop ----------------

	private async void OnListPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_dragStart is not { } start || sender is not ListBox list || _tab is null || _tab.IsTrash)
			return;
		var point = e.GetCurrentPoint(list);
		if (!point.Properties.IsLeftButtonPressed)
		{
			_dragStart = null;
			return;
		}
		var delta = point.Position - start;
		if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8)
			return;
		_dragStart = null;

		var paths = _tab.SelectedItems.Select(i => i.FullPath).ToList();
		if (paths.Count == 0)
			return;

		var data = new DataObject();
		data.Set(InternalDragFormat, string.Join("\n", paths));
		data.Set(DataFormats.Text, string.Join("\n", paths));
		var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
		if (storage is not null)
		{
			var items = new List<Avalonia.Platform.Storage.IStorageItem>();
			foreach (var p in paths)
			{
				var si = Directory.Exists(p) ? (Avalonia.Platform.Storage.IStorageItem?)await storage.TryGetFolderFromPathAsync(new Uri(p)) : await storage.TryGetFileFromPathAsync(new Uri(p));
				if (si is not null)
					items.Add(si);
			}
			if (items.Count > 0)
				data.Set(DataFormats.Files, items);
		}
		await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
	}

	internal static IReadOnlyList<string> GetDroppedPaths(IDataObject data)
	{
		if (data.Get(InternalDragFormat) is string internalPaths)
			return internalPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		if (data.GetFiles() is { } files)
			return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
		return [];
	}

	private void OnDragOver(object? sender, DragEventArgs e)
	{
		var target = DropTargetFolder(e.Source);
		if (_tab is null || target is null || GetDroppedPaths(e.Data).Count == 0)
		{
			e.DragEffects = DragDropEffects.None;
			return;
		}
		e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move;
	}

	private string? DropTargetFolder(object? source)
	{
		if (_tab is null)
			return null;
		if (ItemFromSource(source) is { IsDirectory: true, TrashEntry: null } folder)
			return folder.FullPath;
		return _tab.IsFolder && !_tab.IsSearchResults ? _tab.CurrentPath : null;
	}

	private async void OnDrop(object? sender, DragEventArgs e)
	{
		var target = DropTargetFolder(e.Source);
		var paths = GetDroppedPaths(e.Data);
		if (_tab is null || target is null || paths.Count == 0)
			return;
		if (paths.Contains(target))
			return;
		e.Handled = true;
		await _tab.DropAsync(paths, target, e.KeyModifiers.HasFlag(KeyModifiers.Control));
	}

	// ---------------- Details header ----------------

	private void BuildHeader()
	{
		DetailsHeader.Children.Clear();
		AddHeaderCell(DetailsColumns.Name, Loc.Get("Name", "Name"), SortColumn.Name);
		AddHeaderCell(DetailsColumns.Location, _tab?.IsTrash == true ? Loc.Get("OriginalPath", "Original path") : Loc.Get("Path", "Path"), null);
		AddHeaderCell(DetailsColumns.Modified, _tab?.IsTrash == true ? Loc.Get("DateDeleted", "Date deleted") : Loc.Get("DateModifiedLowerCase", "Date modified"), SortColumn.DateModified);
		AddHeaderCell(DetailsColumns.Type, Loc.Get("Type", "Type"), SortColumn.Type);
		AddHeaderCell(DetailsColumns.Size, Loc.Get("Size", "Size"), SortColumn.Size);
	}

	private void AddHeaderCell(int column, string title, SortColumn? sort)
	{
		var settings = SettingsService.Current;
		var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
		content.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
		if (sort is { } s && settings.SortBy == s)
			content.Children.Add(new SymbolIcon { Symbol = settings.SortDescending ? Symbol.ChevronDown : Symbol.ChevronUp, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });

		var button = new Button { Content = content, Classes = { "column" }, Margin = new Thickness(column == DetailsColumns.Name ? 0 : 0, 0, 0, 0) };
		if (sort is { } sc)
			button.Click += (_, _) => _tab?.SetSortCommand.Execute(sc);

		var thumb = new Thumb
		{
			Width = 6,
			HorizontalAlignment = HorizontalAlignment.Right,
			Cursor = new Cursor(StandardCursorType.SizeWestEast),
			Background = Brushes.Transparent,
			Template = new Avalonia.Controls.Templates.FuncControlTemplate<Thumb>((_, _) => new Border
			{
				Background = Brushes.Transparent,
				Child = new Rectangle { Width = 1, Margin = new Thickness(0, 8), HorizontalAlignment = HorizontalAlignment.Center }.WithResource(Shape.FillProperty, "App.Theme.DividerBrush"),
			}),
		};
		thumb.DragDelta += (_, e) => DetailsColumns.Set(column, DetailsColumns.Get(column) + e.Vector.X);

		var cell = new Panel { Height = 32 };
		cell.Children.Add(button);
		cell.Children.Add(thumb);
		DetailsHeader.Children.Add(cell);
	}

	private void SyncHeaderScroll()
	{
		if (DetailsList.Scroll is ScrollViewer sv)
			HeaderScroller.Offset = new Vector(sv.Offset.X, 0);
	}

	// ---------------- Menus ----------------

	private void BuildSortMenu(ItemCollection items)
	{
		items.Clear();
		if (_tab is null)
			return;
		var s = SettingsService.Current;
		void Add(string text, SortColumn col) => items.Add(Radio(text, s.SortBy == col, () =>
		{
			s.SortBy = col;
			SettingsService.Save();
			_tab.Main.OnSortChanged();
			BuildHeader();
		}));
		Add(Loc.Get("Name", "Name"), SortColumn.Name);
		Add(Loc.Get("DateModifiedLowerCase", "Date modified"), SortColumn.DateModified);
		Add(Loc.Get("DateCreated", "Date created"), SortColumn.DateCreated);
		Add(Loc.Get("Type", "Type"), SortColumn.Type);
		Add(Loc.Get("Size", "Size"), SortColumn.Size);
		items.Add(Separator());
		items.Add(Radio(Loc.Get("Ascending", "Ascending"), !s.SortDescending, () => { _tab.SetSortDirectionCommand.Execute(false); BuildHeader(); }));
		items.Add(Radio(Loc.Get("Descending", "Descending"), s.SortDescending, () => { _tab.SetSortDirectionCommand.Execute(true); BuildHeader(); }));
		items.Add(Separator());
		items.Add(Check(Loc.Get("SettingsListAndSortDirectoriesAlongsideFiles", "Folders first"), s.FoldersFirst, () =>
		{
			s.FoldersFirst = !s.FoldersFirst;
			SettingsService.Save();
			_tab.Main.OnSortChanged();
		}));
	}

	private void BuildLayoutMenu(ItemCollection items)
	{
		items.Clear();
		if (_tab is null)
			return;
		void Add(string text, LayoutMode mode, Symbol icon, string gesture) => items.Add(Radio(text, _tab.Layout == mode, () => _tab.SetLayoutCommand.Execute(mode), icon, gesture));
		Add(Loc.Get("Details", "Details"), LayoutMode.Details, Symbol.TextBulletList, "Ctrl+Shift+1");
		Add(Loc.Get("List", "List"), LayoutMode.List, Symbol.AppsList, "Ctrl+Shift+2");
		Add(Loc.Get("SmallIcons", "Small icons"), LayoutMode.GridSmall, Symbol.Grid, "Ctrl+Shift+3");
		Add(Loc.Get("MediumIcons", "Medium icons"), LayoutMode.GridMedium, Symbol.Grid, "Ctrl+Shift+4");
		Add(Loc.Get("LargeIcons", "Large icons"), LayoutMode.GridLarge, Symbol.Grid, "Ctrl+Shift+5");
	}

	private void BuildMoreMenu(ItemCollection items)
	{
		items.Clear();
		if (_tab is null)
			return;
		var main = _tab.Main;
		var s = SettingsService.Current;
		items.Add(Item(Loc.Get("SelectAll", "Select all"), Symbol.SelectAllOn, _tab.SelectAllCommand, gesture: "Ctrl+A"));
		items.Add(Item(Loc.Get("InvertSelection", "Invert selection"), Symbol.SelectAllOff, _tab.InvertSelectionCommand));
		items.Add(Item(Loc.Get("ClearSelection", "Clear selection"), Symbol.Dismiss, _tab.ClearSelectionCommand));
		items.Add(Separator());
		items.Add(Check(Loc.Get("HiddenItems", "Hidden items"), s.ShowHiddenItems, () => main.ToggleHiddenItemsCommand.Execute(null), "Ctrl+H"));
		items.Add(Check(Loc.Get("ShowFileExtensions", "File extensions"), s.ShowFileExtensions, () => main.ToggleFileExtensionsCommand.Execute(null)));
		items.Add(Separator());
		items.Add(Item(Loc.Get("OpenInTerminal", "Open in Terminal"), Symbol.WindowConsole, _tab.OpenInTerminalCommand, gesture: "Ctrl+Alt+T"));
		if (_tab.IsFolder)
		{
			var pinned = main.Sidebar.IsPinned(_tab.CurrentPath!);
			items.Add(pinned
				? Action(Loc.Get("UnpinFolderFromSidebar", "Unpin from Sidebar"), Symbol.PinOff, () => { main.Sidebar.Unpin(_tab.CurrentPath!); main.OnPinnedChanged(); })
				: Action(Loc.Get("PinFolderToSidebar", "Pin to Sidebar"), Symbol.Pin, () => { main.Sidebar.Pin(_tab.CurrentPath!); main.OnPinnedChanged(); }));
		}
		items.Add(Item(Loc.Get("Properties", "Properties"), Symbol.Info, _tab.ShowPropertiesCommand, gesture: "Alt+Enter"));
		items.Add(Separator());
		items.Add(Item(Loc.Get("Settings", "Settings"), Symbol.Settings, main.OpenSettingsCommand, gesture: "Ctrl+OemComma"));
		Tidy(items);
	}

	private void OnListContextRequested(object? sender, ContextRequestedEventArgs e)
	{
		if (_tab is null || sender is not ListBox list)
			return;
		e.Handled = true;
		var item = ItemFromSource(e.Source);
		var menu = new ContextMenu();
		if (item is null)
			BuildBackgroundMenu(menu.Items);
		else
			BuildItemMenu(menu.Items);
		Tidy(menu.Items);
		menu.Open(list);
	}

	private void BuildBackgroundMenu(ItemCollection items)
	{
		var tab = _tab!;
		if (tab.IsHome)
			return;

		var layout = Sub(Loc.Get("Layout", "Layout"), Symbol.Grid);
		BuildLayoutMenu(layout.Items);
		var sort = Sub(Loc.Get("SortBy", "Sort by"), Symbol.ArrowSort);
		BuildSortMenu(sort.Items);
		items.Add(layout);
		items.Add(sort);
		items.Add(Item(Loc.Get("Refresh", "Refresh"), Symbol.ArrowClockwise, tab.RefreshCommand, gesture: "F5"));
		items.Add(Separator());

		if (tab.IsTrash)
		{
			items.Add(Item(Loc.Get("RestoreAllItems", "Restore all items"), Symbol.ArrowUndo, tab.RestoreAllCommand));
			items.Add(Item(Loc.Get("EmptyTrash", "Empty trash"), Symbol.DeleteDismiss, tab.EmptyTrashCommand));
			return;
		}

		if (tab.CanCreateItems)
		{
			items.Add(Item(Loc.Get("Paste", "Paste"), Symbol.ClipboardPaste, tab.PasteCommand, gesture: "Ctrl+V"));
			items.Add(Sub(Loc.Get("BaseLayoutContextFlyoutNew.Label", "New"), Symbol.Add,
				Item(Loc.Get("Folder", "Folder"), Symbol.Folder, tab.NewFolderCommand, gesture: "Ctrl+Shift+N", iconBrush: new SolidColorBrush(Color.Parse("#FFC83D"))),
				Item(Loc.Get("File", "File"), Symbol.DocumentAdd, tab.NewFileCommand)));
			items.Add(Separator());
		}
		items.Add(Item(Loc.Get("OpenInTerminal", "Open in Terminal"), Symbol.WindowConsole, tab.OpenInTerminalCommand));
		items.Add(Item(Loc.Get("CopyPath", "Copy path"), Symbol.Copy, tab.CopyPathCommand, gesture: "Ctrl+Shift+C"));
		if (tab.IsFolder && !tab.Main.Sidebar.IsPinned(tab.CurrentPath!))
			items.Add(Action(Loc.Get("PinFolderToSidebar", "Pin to Sidebar"), Symbol.Pin, () => { tab.Main.Sidebar.Pin(tab.CurrentPath!); tab.Main.OnPinnedChanged(); }));
		items.Add(Separator());
		items.Add(Item(Loc.Get("SelectAll", "Select all"), Symbol.SelectAllOn, tab.SelectAllCommand, gesture: "Ctrl+A"));
		items.Add(Item(Loc.Get("Properties", "Properties"), Symbol.Info, tab.ShowPropertiesCommand, gesture: "Alt+Enter"));
	}

	private void BuildItemMenu(ItemCollection items)
	{
		var tab = _tab!;
		var selection = tab.SelectedItems;
		if (selection.Count == 0)
			return;

		if (tab.IsTrash)
		{
			items.Add(Item(Loc.Get("Restore", "Restore"), Symbol.ArrowUndo, tab.RestoreCommand));
			items.Add(Item(Loc.Get("DeletePermanently", "Delete permanently"), Symbol.Delete, tab.DeletePermanentlyCommand, gesture: "Delete"));
			items.Add(Separator());
			items.Add(Item(Loc.Get("Properties", "Properties"), Symbol.Info, tab.ShowPropertiesCommand, gesture: "Alt+Enter"));
			return;
		}

		var single = selection.Count == 1 ? selection[0] : null;
		var open = Item(Loc.Get("Open", "Open"), Symbol.Open, tab.OpenSelectedCommand, gesture: "Enter");
		open.FontWeight = FontWeight.SemiBold;
		items.Add(open);

		if (single is { IsDirectory: false })
		{
			var openWith = Sub(Loc.Get("OpenWith", "Open with"), Symbol.AppGeneric);
			var apps = LauncherService.GetAppsFor(single.FullPath);
			foreach (var app in apps.Take(15))
				openWith.Items.Add(Action(app.Name, null, () => LauncherService.LaunchApp(app, [single.FullPath])));
			if (apps.Count == 0)
				openWith.Items.Add(new MenuItem { Header = "No applications found", IsEnabled = false });
			items.Add(openWith);
		}

		if (selection.All(i => i.IsDirectory))
		{
			items.Add(Item(Loc.Get("OpenInNewTab", "Open in new tab"), Symbol.TabAdd, tab.OpenInNewTabCommand));
			if (single is not null)
				items.Add(Item(Loc.Get("OpenInTerminal", "Open in Terminal"), Symbol.WindowConsole, tab.OpenInTerminalCommand));
		}
		if (tab.IsSearchResults && single is not null)
			items.Add(Item(Loc.Get("OpenFileLocation", "Open file location"), Symbol.FolderOpen, tab.OpenFileLocationCommand, single));

		items.Add(Separator());
		items.Add(Item(Loc.Get("Cut", "Cut"), Symbol.Cut, tab.CutCommand, gesture: "Ctrl+X"));
		items.Add(Item(Loc.Get("Copy", "Copy"), Symbol.Copy, tab.CopyCommand, gesture: "Ctrl+C"));
		if (single is { IsDirectory: true })
			items.Add(Action(Loc.Get("PasteItemsHere", "Paste into folder"), Symbol.ClipboardPaste, async () =>
			{
				var (paths, cut) = await ClipboardService.GetAsync();
				if (paths.Count > 0)
				{
					await tab.Main.TransferAsync(paths, single.FullPath, cut);
					if (cut)
						ClipboardService.Clear();
				}
			}, isEnabled: ClipboardService.HasItems));
		items.Add(Item(Loc.Get("CopyPath", "Copy path"), Symbol.DocumentCopy, tab.CopyPathCommand, gesture: "Ctrl+Shift+C"));
		items.Add(Item(Loc.Get("Rename", "Rename"), Symbol.Rename, tab.RenameCommand, gesture: "F2", isEnabled: single is not null));
		items.Add(Item(Loc.Get("Delete", "Delete"), Symbol.Delete, tab.DeleteCommand, gesture: "Delete"));
		items.Add(Separator());

		items.Add(Item(Loc.Get("CompressIntoZip", "Compress to ZIP"), Symbol.FolderZip, tab.CompressCommand));
		if (tab.SelectionIsArchive)
			items.Add(Item(Loc.Get("ExtractHere", "Extract here"), Symbol.FolderArrowUp, tab.ExtractCommand));

		if (selection.All(i => i.IsDirectory))
		{
			items.Add(tab.IsSelectionPinned
				? Item(Loc.Get("UnpinFolderFromSidebar", "Unpin from Sidebar"), Symbol.PinOff, tab.UnpinFromSidebarCommand)
				: Item(Loc.Get("PinFolderToSidebar", "Pin to Sidebar"), Symbol.Pin, tab.PinToSidebarCommand));
		}
		items.Add(Separator());
		items.Add(Item(Loc.Get("Properties", "Properties"), Symbol.Info, tab.ShowPropertiesCommand, gesture: "Alt+Enter"));
	}

	// ---------------- Keyboard ----------------

	/// <summary>Type-to-find: jumps to the first item whose name starts with the typed text.</summary>
	public bool HandleTypeAhead(string text)
	{
		if (_tab is null || _tab.IsHome || string.IsNullOrEmpty(text) || char.IsControl(text[0]))
			return false;
		var now = Environment.TickCount64;
		_typeAhead = now - _typeAheadTime > 1000 ? text : _typeAhead + text;
		_typeAheadTime = now;
		var match = _tab.Items.FirstOrDefault(i => i.Name.StartsWith(_typeAhead, StringComparison.CurrentCultureIgnoreCase));
		if (match is not null)
			ApplySelection([match], scroll: true);
		return match is not null;
	}

	public void OpenContextMenuForSelection()
	{
		if (_tab is null)
			return;
		var menu = new ContextMenu();
		if (_tab.HasSelection)
			BuildItemMenu(menu.Items);
		else
			BuildBackgroundMenu(menu.Items);
		Tidy(menu.Items);
		menu.Open(ActiveList);
	}
}
