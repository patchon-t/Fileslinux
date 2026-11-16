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
	private MainWindowViewModel? _main;
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
			list.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ClearDropFeedback());
			list.AddHandler(PointerWheelChangedEvent, OnListPointerWheel, RoutingStrategies.Tunnel);
			// Group headers: styled differently, never selected; a click collapses or expands the group.
			list.ContainerPrepared += (_, e) => e.Container.Classes.Set("groupheader", e.Container.DataContext is GroupHeader);
			list.AddHandler(PointerCaptureLostEvent, (_, _) => EndMarquee());
		}

		DetailsList.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => SyncHeaderScroll());
		// Column widths: the header and rows re-measure themselves (DetailsRowPanel). Rebuilding the header here
		// would destroy the grip being dragged and stop the resize after the first pixel.
		// Tooltips show the keyboard shortcut, like the Windows toolbar ("Copy (Ctrl+C)").
		foreach (var (button, key, fallback, shortcut) in new (Button, string, string, string)[]
		{
			(CutButton, "Cut", "Cut", "Ctrl+X"),
			(CopyButton, "Copy", "Copy", "Ctrl+C"),
			(PasteButton, "Paste", "Paste", "Ctrl+V"),
			(RenameButton, "Rename", "Rename", "F2"),
			(DeleteButton, "Delete", "Delete", "Delete"),
		})
		{
			ToolTip.SetTip(button, $"{Loc.Get(key, fallback)} ({shortcut})");
		}

		// These menus depend on the current state, so a fresh menu is built on every click.
		// (Filling a MenuFlyout from its Opening event leaves it empty on screen.)
		SortButton.Click += (_, _) => ShowMenu(SortButton, BuildSortMenu);
		LayoutButton.Click += (_, _) => ShowMenu(LayoutButton, BuildLayoutMenu);
		MoreButton.Click += (_, _) => ShowMenu(MoreButton, BuildMoreMenu);
		// The details pane lives in a sized column so the splitter can resize it.
		InfoSplitter.DragCompleted += (_, _) =>
		{
			SettingsService.Current.InfoPaneWidth = BodyGrid.ColumnDefinitions[2].ActualWidth;
			SettingsService.Save();
		};
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
			_lastLocation = (_tab.CurrentPath ?? "home") + "|" + _tab.IsSearchResults;
			if (_main != _tab.Main)
			{
				if (_main is not null)
					_main.PropertyChanged -= OnMainPropertyChanged;
				_main = _tab.Main;
				_main.PropertyChanged += OnMainPropertyChanged;
			}
			UpdateInfoPaneColumn();
			Dispatcher.UIThread.Post(PlayEntranceAfterLayout, DispatcherPriority.Background);
			DetailsColumns.SetShowLocation(_tab.ShowLocationColumn);
			BuildHeader();
			Dispatcher.UIThread.Post(() => ApplySelection(_tab.SelectedItems), DispatcherPriority.Background);
		}
	}

	/// <summary>Opens or closes the details pane column (resizable with the splitter, width remembered).</summary>
	private void UpdateInfoPaneColumn()
	{
		var column = BodyGrid.ColumnDefinitions[2];
		if (_main?.IsInfoPaneOpen == true)
		{
			column.MinWidth = 200;
			column.MaxWidth = 900;
			column.Width = new GridLength(Math.Clamp(SettingsService.Current.InfoPaneWidth, 200, 900));
		}
		else
		{
			column.MinWidth = 0;
			column.Width = new GridLength(0);
		}
	}

	private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MainWindowViewModel.IsInfoPaneOpen))
			UpdateInfoPaneColumn();
		// The details pane slides in from the right when opened.
		if (e.PropertyName == nameof(MainWindowViewModel.IsInfoPaneOpen) && _main?.IsInfoPaneOpen == true)
			Dispatcher.UIThread.Post(() => _ = Motion.SlideInAsync(InfoPane, 40), DispatcherPriority.Background);
	}

	private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(ShellTabViewModel.ShowLocationColumn) or nameof(ShellTabViewModel.IsSearchResults) or nameof(ShellTabViewModel.CurrentPath) or nameof(ShellTabViewModel.SortBy) or nameof(ShellTabViewModel.SortDescending))
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
				PlayEntranceAfterLayout();
			}, DispatcherPriority.Background);
		}
	}

	private void OnNavigated(object? sender, EventArgs e)
	{
		// Refreshes (file watcher, F5) keep the same location and must not replay the entrance.
		var location = (_tab?.CurrentPath ?? "home") + "|" + _tab?.IsSearchResults;
		var isNewLocation = location != _lastLocation;
		_lastLocation = location;

		Dispatcher.UIThread.Post(() =>
		{
			var list = ActiveList;
			if (list.Scroll is ScrollViewer sv && _tab?.SelectedItems.Count == 0)
				sv.Offset = default;
			if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox)
				FocusActiveList();
			if (isNewLocation)
				PlayEntranceAfterLayout();
		}, DispatcherPriority.Background);
	}

	private string? _lastLocation;
	private string? _lastEntranceKey;
	private long _lastEntranceTime;

	/// <summary>Waits for the list to create rows for the new items, then plays the entrance on those rows.</summary>
	private void PlayEntranceAfterLayout()
	{
		if (_tab is null || !Motion.IsEnabled)
			return;
		if (_tab.IsHome)
		{
			PlayEntrance();
			return;
		}
		var list = ActiveList;
		void OnLayoutUpdated(object? sender, EventArgs e)
		{
			list.LayoutUpdated -= OnLayoutUpdated;
			PlayEntrance();
		}
		list.LayoutUpdated += OnLayoutUpdated;
		list.InvalidateMeasure();
	}

	/// <summary>
	/// Items fade and rise into place, staggered, like a WinUI ListView/GridView loading (EntranceThemeTransition).
	/// The Home page plays the same entrance for its widgets.
	/// </summary>
	public void PlayEntrance()
	{
		if (_tab is null || !Motion.IsEnabled)
			return;

		// Opening a tab can trigger this twice (tab switch + first load); play it once.
		var key = (_tab.CurrentPath ?? "home") + "|" + _tab.IsSearchResults + "|" + _tab.Layout;
		var now = Environment.TickCount64;
		if (key == _lastEntranceKey && now - _lastEntranceTime < 150)
			return;
		_lastEntranceKey = key;
		_lastEntranceTime = now;
		if (_tab.IsHome)
		{
			HomeHost.PlayEntrance();
			return;
		}
		var list = ActiveList;
		var items = _tab.Items.ToHashSet();
		var containers = list.GetRealizedContainers()
			.Where(c => c.DataContext is FileItem item && items.Contains(item))
			.OrderBy(list.IndexFromContainer)
			.Cast<Visual>()
			.ToList();
		if (containers.Count == 0)
		{
			_ = Motion.FadeInAsync(list);
			return;
		}
		if (list == DetailsList)
			_ = Motion.FadeInAsync(DetailsHeader);
		Motion.StaggeredEntrance(containers);
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
		// Keyboard or Shift+click ranges can include group headers: drop them from the selection.
		if (list.SelectedItems?.OfType<GroupHeader>().ToList() is { Count: > 0 } headers)
		{
			_syncingSelection = true;
			foreach (var h in headers)
				list.SelectedItems.Remove(h);
			_syncingSelection = false;
		}
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

		if (point.Properties.IsLeftButtonPressed && ContainerFromSource(e.Source) is { DataContext: GroupHeader header })
		{
			_tab?.ToggleGroup(header);
			e.Handled = true;
			return;
		}

		var item = ItemFromSource(e.Source);
		// In Details the rows are full width; the space right of the last column also starts a drag-select box.
		var pastColumns = list == DetailsList && point.Position.X > DetailsColumns.TotalWidth + 16;
		if (point.Properties.IsLeftButtonPressed)
		{
			if (item is not null && !pastColumns)
			{
				_dragStart = point.Position;
				return;
			}
			_dragStart = null;
			if (e.KeyModifiers == KeyModifiers.None)
				ApplySelection([]); // Click on empty space clears the selection.
			BeginMarquee(list, e);
			if (pastColumns)
				e.Handled = true; // Don't let the row under the pointer get selected.
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

	private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		_dragStart = null;
		if (_marqueeList is not null)
		{
			e.Pointer.Capture(null);
			EndMarquee();
		}
	}

	private void OnListPointerWheel(object? sender, PointerWheelEventArgs e)
	{
		// Ctrl + mouse wheel makes items bigger or smaller, like Windows.
		if (_tab is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
			return;
		_tab.StepLayout(e.Delta.Y > 0 ? 1 : -1);
		e.Handled = true;
	}

	// ---------------- Drag-select box (rubber band) ----------------

	private ListBox? _marqueeList;
	private Point _marqueeStart;
	private Point _marqueeLast;
	private bool _marqueeActive;
	private HashSet<FileItem> _marqueeBase = [];
	private readonly Dictionary<FileItem, Rect> _marqueeSeen = [];
	private DispatcherTimer? _marqueeScrollTimer;

	private static Vector ScrollOffset(ListBox list) => list.Scroll is ScrollViewer sv ? sv.Offset : default;

	/// <summary>Starts a possible drag-select box. It shows once the pointer moves a few pixels.</summary>
	private void BeginMarquee(ListBox list, PointerPressedEventArgs e)
	{
		_marqueeList = list;
		// Points are kept in content coordinates (including the scroll offset) so the box survives scrolling.
		_marqueeStart = e.GetPosition(list) + ScrollOffset(list);
		_marqueeLast = e.GetPosition(list);
		_marqueeActive = false;
		_marqueeSeen.Clear();
		_marqueeBase = e.KeyModifiers.HasFlag(KeyModifiers.Control) && _tab is not null ? _tab.SelectedItems.ToHashSet() : [];
		e.Pointer.Capture(list);
	}

	private void UpdateMarquee(PointerEventArgs e)
	{
		if (_marqueeList is not { } list || _tab is null)
			return;
		_marqueeLast = e.GetPosition(list);
		var current = _marqueeLast + ScrollOffset(list);
		if (!_marqueeActive)
		{
			var d = current - _marqueeStart;
			if (Math.Abs(d.X) < 4 && Math.Abs(d.Y) < 4)
				return;
			_marqueeActive = true;
			Marquee.IsVisible = true;
		}

		// Keep scrolling while the pointer is held above or below the list.
		var outside = _marqueeLast.Y < 0 || _marqueeLast.Y > list.Bounds.Height;
		if (outside && _marqueeScrollTimer is null)
		{
			_marqueeScrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) => AutoScrollMarquee());
			_marqueeScrollTimer.Start();
		}
		else if (!outside)
		{
			_marqueeScrollTimer?.Stop();
			_marqueeScrollTimer = null;
		}
		RefreshMarquee();
	}

	private void AutoScrollMarquee()
	{
		if (_marqueeList?.Scroll is not ScrollViewer sv)
			return;
		var y = _marqueeLast.Y;
		var h = _marqueeList.Bounds.Height;
		var step = y < 0 ? Math.Max(-40, y) : y > h ? Math.Min(40, y - h) : 0;
		if (step == 0)
			return;
		sv.Offset = new Vector(sv.Offset.X, Math.Clamp(sv.Offset.Y + step, 0, Math.Max(0, sv.Extent.Height - sv.Viewport.Height)));
		RefreshMarquee();
	}

	private void RefreshMarquee()
	{
		if (_marqueeList is not { } list || _tab is null)
			return;
		var offset = ScrollOffset(list);
		var current = _marqueeLast + offset;
		var box = new Rect(_marqueeStart, current).Normalize();

		// Draw the box in the overlay (view coordinates).
		var topLeft = list.TranslatePoint(new Point(box.X - offset.X, box.Y - offset.Y), OverlayLayer) ?? default;
		var visible = new Rect(topLeft, box.Size).Intersect(new Rect(OverlayLayer.Bounds.Size));
		Canvas.SetLeft(Marquee, visible.X);
		Canvas.SetTop(Marquee, visible.Y);
		Marquee.Width = Math.Max(0, visible.Width);
		Marquee.Height = Math.Max(0, visible.Height);

		// Remember where each realized item is (content coordinates); items scrolled out keep their last position.
		foreach (var container in list.GetRealizedContainers())
		{
			if (container.DataContext is FileItem item && container.TranslatePoint(default, list) is { } p)
				_marqueeSeen[item] = new Rect(p + offset, container.Bounds.Size);
		}
		var hits = _marqueeSeen.Where(kv => kv.Value.Intersects(box)).Select(kv => kv.Key);
		var selection = _marqueeBase.Count > 0 ? _marqueeBase.Union(hits).ToList() : hits.ToList();
		var ordered = _tab.Items.Where(selection.Contains).ToList();
		if (!ordered.SequenceEqual(_tab.SelectedItems))
			ApplySelection(ordered);
	}

	private void EndMarquee()
	{
		_marqueeScrollTimer?.Stop();
		_marqueeScrollTimer = null;
		_marqueeList = null;
		_marqueeActive = false;
		Marquee.IsVisible = false;
	}

	/// <summary>True while a drag-select box is on screen (used by tests).</summary>
	internal bool IsMarqueeActive => _marqueeActive;

	// ---------------- Drag and drop ----------------

	private async void OnListPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_marqueeList is not null)
		{
			UpdateMarquee(e);
			return;
		}
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
		var paths = GetDroppedPaths(e.Data);
		if (_tab is null || target is null || paths.Count == 0 || paths.Contains(target) || paths.All(p => System.IO.Path.GetDirectoryName(p) == target))
		{
			e.DragEffects = DragDropEffects.None;
			ClearDropFeedback();
			return;
		}

		// Same rule as the drop: move within a drive, copy across drives, Ctrl always copies.
		var copy = e.KeyModifiers.HasFlag(KeyModifiers.Control) || paths.Any(p => MountService.GetMountPoint(p) != MountService.GetMountPoint(target));
		e.DragEffects = copy ? DragDropEffects.Copy : DragDropEffects.Move;

		// Highlight the folder under the pointer, and show "Move to Documents" next to the pointer, as Windows does.
		var container = ContainerFromSource(e.Source);
		if (container is not { DataContext: FileItem { IsDirectory: true } })
			container = null;
		if (container != _dropTarget)
		{
			_dropTarget?.Classes.Remove("droptarget");
			_dropTarget = container;
			_dropTarget?.Classes.Add("droptarget");
		}
		var name = System.IO.Path.GetFileName(target.TrimEnd('/')) is { Length: > 0 } n ? n : target;
		DragCaptionText.Text = copy ? $"{Loc.Get("CopyTo", "Copy to")} {name}" : $"{Loc.Get("MoveTo", "Move to")} {name}";
		DragCaptionIcon.Symbol = copy ? Symbol.Copy : Symbol.ArrowMove;
		var at = e.GetPosition(OverlayLayer);
		Canvas.SetLeft(DragCaption, at.X + 16);
		Canvas.SetTop(DragCaption, at.Y + 18);
		DragCaption.IsVisible = true;
	}

	private ListBoxItem? _dropTarget;

	private void ClearDropFeedback()
	{
		_dropTarget?.Classes.Remove("droptarget");
		_dropTarget = null;
		DragCaption.IsVisible = false;
	}

	private static ListBoxItem? ContainerFromSource(object? source)
	{
		var visual = source as Visual;
		while (visual is not null and not ListBox)
		{
			if (visual is ListBoxItem container)
				return container;
			visual = visual.GetVisualParent();
		}
		return null;
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
		ClearDropFeedback();
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
		var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
		content.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
		if (sort is { } s && _tab?.SortBy == s)
			content.Children.Add(new SymbolIcon { Symbol = _tab.SortDescending ? Symbol.ChevronDown : Symbol.ChevronUp, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });

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
		thumb.DragCompleted += (_, _) => DetailsColumns.Save();

		// Clip each cell to its column: a hidden column (width 0, e.g. Location) would otherwise leave its
		// resize grip on top of the neighbouring column's grip and swallow every drag.
		var cell = new Panel { Height = 32, ClipToBounds = true };
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

	/// <summary>The menu most recently opened from the toolbar (used by tests).</summary>
	internal MenuFlyout? LastToolbarMenu { get; private set; }

	private void ShowMenu(Control anchor, Action<ItemCollection> build)
	{
		var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
		build(menu.Items);
		LastToolbarMenu = menu;
		menu.ShowAt(anchor);
	}

	private void BuildSortMenu(ItemCollection items) => BuildSortMenu(items, includeGroupBy: true);

	private void BuildSortMenu(ItemCollection items, bool includeGroupBy)
	{
		items.Clear();
		if (_tab is null)
			return;
		var s = SettingsService.Current;
		var tab = _tab;
		// Sorting applies to this folder only (see FolderViewService).
		void Add(string text, SortColumn col) => items.Add(Radio(text, tab.SortBy == col, () => tab.SetSortCommand.Execute(col)));
		Add(Loc.Get("Name", "Name"), SortColumn.Name);
		Add(Loc.Get("DateModifiedLowerCase", "Date modified"), SortColumn.DateModified);
		Add(Loc.Get("DateCreated", "Date created"), SortColumn.DateCreated);
		Add(Loc.Get("Type", "Type"), SortColumn.Type);
		Add(Loc.Get("Size", "Size"), SortColumn.Size);
		items.Add(Separator());
		items.Add(Radio(Loc.Get("Ascending", "Ascending"), !tab.SortDescending, () => tab.SetSortDirectionCommand.Execute(false)));
		items.Add(Radio(Loc.Get("Descending", "Descending"), tab.SortDescending, () => tab.SetSortDirectionCommand.Execute(true)));
		items.Add(Separator());
		items.Add(Check(Loc.Get("SettingsListAndSortDirectoriesAlongsideFiles", "Folders first"), s.FoldersFirst, () =>
		{
			s.FoldersFirst = !s.FoldersFirst;
			SettingsService.Save();
			_tab.Main.OnSortChanged();
		}));
		if (!includeGroupBy)
			return;
		items.Add(Separator());
		var group = Sub(Loc.Get("GroupBy", "Group by"), Symbol.GroupList);
		BuildGroupMenu(group.Items);
		items.Add(group);
	}

	/// <summary>Group by: this folder only (Downloads starts grouped by date modified, like Windows).</summary>
	private void BuildGroupMenu(ItemCollection items)
	{
		items.Clear();
		if (_tab is null)
			return;
		var tab = _tab;
		void Add(string text, GroupColumn column) => items.Add(Radio(text, tab.GroupBy == column, () => tab.SetGroupCommand.Execute(column)));
		Add(Loc.Get("None", "None"), GroupColumn.None);
		Add(Loc.Get("Name", "Name"), GroupColumn.Name);
		Add(Loc.Get("DateModifiedLowerCase", "Date modified"), GroupColumn.DateModified);
		Add(Loc.Get("DateCreated", "Date created"), GroupColumn.DateCreated);
		Add(Loc.Get("Type", "Type"), GroupColumn.Type);
		Add(Loc.Get("Size", "Size"), GroupColumn.Size);
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
		if (_tab.HasCustomView)
		{
			// The folder has its own view; offer to go back to the default from Settings.
			items.Add(Separator());
			items.Add(Item(Loc.Get("UseDefaultLayout", "Use the default view for this folder"), Symbol.ArrowReset, _tab.ResetFolderViewCommand));
		}
	}

	private void BuildMoreMenu(ItemCollection items)
	{
		items.Clear();
		if (_tab is null)
			return;
		var main = _tab.Main;
		var s = SettingsService.Current;
		items.Add(Item(UndoService.UndoDescription is { } undo ? $"{Loc.Get("Undo", "Undo")}: {undo}" : Loc.Get("Undo", "Undo"), Symbol.ArrowUndo, main.UndoCommand, gesture: "Ctrl+Z", isEnabled: UndoService.CanUndo));
		items.Add(Item(UndoService.RedoDescription is { } redo ? $"{Loc.Get("Redo", "Redo")}: {redo}" : Loc.Get("Redo", "Redo"), Symbol.ArrowRedo, main.RedoCommand, gesture: "Ctrl+Y", isEnabled: UndoService.CanRedo));
		items.Add(Separator());
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
		BuildSortMenu(sort.Items, includeGroupBy: false);
		var groupBy = Sub(Loc.Get("GroupBy", "Group by"), Symbol.GroupList);
		BuildGroupMenu(groupBy.Items);
		items.Add(layout);
		items.Add(sort);
		items.Add(groupBy);
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
