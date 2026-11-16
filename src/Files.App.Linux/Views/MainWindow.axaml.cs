// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Files.App.Linux.Helpers;
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

		// Clicking anywhere in the address bar except on a folder name starts editing, as on Windows.
		BreadcrumbHost.PointerPressed += (_, e) =>
		{
			if (e.GetCurrentPoint(BreadcrumbHost).Properties.IsLeftButtonPressed && (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null)
			{
				BeginEditPath();
				e.Handled = true;
			}
		};
		PathBox.TextChanged += (_, _) => UpdateSuggestions();
		BreadcrumbHost.ContextRequested += (_, e) =>
		{
			// Right-click on the address bar: Edit address / Copy address, as in Windows Explorer.
			e.Handled = true;
			var menu = new ContextMenu();
			menu.Items.Add(Action(Loc.Get("EditAddress", "Edit address"), Symbol.Rename, BeginEditPath, "Ctrl+L"));
			menu.Items.Add(Action(Loc.Get("CopyAddress", "Copy address"), Symbol.Copy, () => _ = ClipboardService.SetTextAsync(CurrentPathText() is { Length: > 0 } p ? p : XdgPaths.Home)));
			menu.Items.Add(Separator());
			menu.Items.Add(Action(Loc.Get("OpenInTerminal", "Open in Terminal"), Symbol.WindowConsole, () => ExecuteAddress("cmd")));
			menu.Open(BreadcrumbHost);
		};
		SuggestionList.AddHandler(PointerReleasedEvent, (_, e) =>
		{
			if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is AddressSuggestion pick)
				AcceptSuggestion(pick);
		}, RoutingStrategies.Bubble, handledEventsToo: true);
		// Tunnel: the text box would otherwise use Up/Down itself before we can move through the suggestions.
		PathBox.AddHandler(KeyDownEvent, OnPathBoxKeyDown, RoutingStrategies.Tunnel);
		PathBox.LostFocus += (_, _) =>
		{
			// The error dialog takes focus but should come back to the box afterwards; anything else ends editing.
			if (!DialogLayer.IsVisible)
				EndEditPath();
		};
		SearchBox.KeyDown += OnSearchBoxKeyDown;

		SidebarList.AddHandler(PointerReleasedEvent, OnSidebarPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
		SidebarList.ContextRequested += OnSidebarContextRequested;
		SidebarList.KeyDown += OnSidebarKeyDown;
		DragDrop.SetAllowDrop(SidebarList, true);
		SidebarList.AddHandler(DragDrop.DragOverEvent, OnSidebarDragOver);
		SidebarList.AddHandler(DragDrop.DropEvent, OnSidebarDrop);
		SidebarList.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetSidebarDropTarget(null));
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
		Opened += (_, _) => OpenWindows.Add(this);
		Closed += (_, _) =>
		{
			OpenWindows.Remove(this);
			_vm?.Detach();
			SettingsService.Changed -= OnSettingsChanged;
		};

		// Title bar: tabs live in the title bar with our own caption buttons, like Files on Windows.
		MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
		MaximizeButton.Click += (_, _) => ToggleMaximize();
		CloseButton.Click += (_, _) => Close();
		DragArea.PointerPressed += OnDragAreaPointerPressed;
		foreach (var grip in ResizeGrips.Children.OfType<Border>())
			grip.PointerPressed += OnResizeGripPressed;
		SettingsService.Changed += OnSettingsChanged;
		ApplyTitleBar();

		// Tabs: drag to reorder; drag out of the tab bar to open the tab in its own window.
		TabList.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
		AddHandler(PointerMovedEvent, OnTabPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
		AddHandler(PointerReleasedEvent, (_, _) => _draggedTab = null, RoutingStrategies.Tunnel, handledEventsToo: true);

		// The refresh icon spins when you refresh, and keeps spinning while the folder loads.
		RefreshButton.Click += (_, _) => SpinRefreshIcon();
	}

	private void OnSettingsChanged(object? sender, EventArgs e) => ApplyTitleBar();

	/// <summary>All open Files windows (tabs can be dragged out into new ones).</summary>
	public static List<MainWindow> OpenWindows { get; } = [];

	/// <summary>True when the app draws its own title bar (tabs + caption buttons) instead of the desktop's.</summary>
	public bool UsesCustomTitleBar => !SettingsService.Current.UseSystemTitleBar;

	private void ApplyTitleBar()
	{
		var custom = UsesCustomTitleBar;
		SystemDecorations = custom ? SystemDecorations.None : SystemDecorations.Full;
		CaptionButtons.IsVisible = custom;
		DragArea.IsHitTestVisible = custom;
		UpdateWindowFrame();
	}

	private void UpdateWindowFrame()
	{
		var custom = UsesCustomTitleBar;
		var normal = WindowState == WindowState.Normal;
		ResizeGrips.IsVisible = custom && normal;
		WindowOutline.IsVisible = custom && normal;
		MaximizeIcon.Symbol = WindowState == WindowState.Maximized ? FluentIcons.Common.Symbol.SquareMultiple : FluentIcons.Common.Symbol.Maximize;
		ToolTip.SetTip(MaximizeButton, WindowState == WindowState.Maximized ? Loc.Get("Restore", "Restore") : Loc.Get("Maximize", "Maximize"));
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == WindowStateProperty && MaximizeIcon is not null)
			UpdateWindowFrame();
		else if (change.Property == SidebarPaneWidthProperty && ContentGrid is not null)
			ContentGrid.ColumnDefinitions[0].Width = new GridLength(Math.Max(0, SidebarPaneWidth));
	}

	private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

	private void OnDragAreaPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!UsesCustomTitleBar || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;
		if (e.ClickCount == 2)
			ToggleMaximize();
		else
			BeginMoveDrag(e);
		e.Handled = true;
	}

	private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is Border { Tag: string tag } && Enum.TryParse<WindowEdge>(tag, out var edge) && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			BeginResizeDrag(edge, e);
			e.Handled = true;
		}
	}

	// ---------------- Tab dragging ----------------

	private ShellTabViewModel? _draggedTab;
	private Point _tabDragStart;
	private bool _tabDragging;
	private PointerPressedEventArgs? _tabPress;

	/// <summary>How far outside the tab bar (in pixels) a tab must be dragged to come out into its own window.</summary>
	private const double TearOutDistance = 32;

	private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		_draggedTab = null;
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source || source.FindAncestorOfType<Button>(includeSelf: true) is not null)
			return;
		if (source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: ShellTabViewModel tab })
			return;
		_draggedTab = tab;
		_tabDragStart = e.GetPosition(this);
		_tabDragging = false;
		_tabPress = e;
	}

	private void OnTabPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_draggedTab is not { } tab || _vm is null)
			return;
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			_draggedTab = null;
			return;
		}
		var position = e.GetPosition(this);
		if (!_tabDragging)
		{
			var d = position - _tabDragStart;
			if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6)
				return;
			_tabDragging = true;

			// Dragging the only tab moves the window, as on Windows.
			if (_vm.Tabs.Count == 1)
			{
				_draggedTab = null;
				if (UsesCustomTitleBar && _tabPress is not null)
				{
					try
					{
						BeginMoveDrag(_tabPress);
					}
					catch (Exception)
					{
						// Not supported by this window manager.
					}
				}
				return;
			}
		}

		var inBar = TabBar.TranslatePoint(default, this) is { } barOrigin
			? new Rect(barOrigin, TabBar.Bounds.Size).Inflate(new Thickness(0, TearOutDistance))
			: default;
		if (!inBar.Contains(position) || position.X < 0 || position.X > Bounds.Width)
		{
			_draggedTab = null;
			TearOutTab(tab, this.PointToScreen(position));
			return;
		}

		// Reorder: move the tab to the slot under the pointer.
		for (int i = 0; i < _vm.Tabs.Count; i++)
		{
			if (TabList.ContainerFromIndex(i) is { } container && container.TranslatePoint(default, this) is { } origin
				&& position.X >= origin.X && position.X < origin.X + container.Bounds.Width)
			{
				_vm.MoveTab(tab, i);
				break;
			}
		}
	}

	/// <summary>Opens the tab in a new window under the pointer and removes it from this one.</summary>
	public MainWindow? TearOutTab(ShellTabViewModel tab, PixelPoint screenPoint)
	{
		if (_vm is null || _vm.Tabs.Count < 2)
			return null;
		var vm = new MainWindowViewModel();
		var window = new MainWindow { DataContext = vm, Width = Bounds.Width, Height = Bounds.Height };
		var newTab = vm.OpenTab(tab.CurrentPath);
		newTab.CopyHistoryFrom(tab);
		vm.SelectedTab = newTab;
		window.WindowStartupLocation = WindowStartupLocation.Manual;
		window.Position = new PixelPoint(Math.Max(0, screenPoint.X - 120), Math.Max(0, screenPoint.Y - 20));
		_vm.DetachTab(tab);
		window.Show();
		window.Activate();
		return window;
	}

	private bool _spinning;

	/// <summary>One full turn of the refresh arrow (and more turns while the folder is still loading).</summary>
	public async void SpinRefreshIcon()
	{
		if (_spinning || !Motion.IsEnabled)
			return;
		_spinning = true;
		try
		{
			do
			{
				await Motion.SpinAsync(RefreshIcon);
			}
			while (_vm?.SelectedTab?.IsLoading == true && IsVisible);
		}
		finally
		{
			_spinning = false;
		}
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

	/// <summary>Animated width of the sidebar column (the Windows sidebar slides open and closed in 350 ms).</summary>
	public static readonly StyledProperty<double> SidebarPaneWidthProperty =
		AvaloniaProperty.Register<MainWindow, double>(nameof(SidebarPaneWidth));

	public double SidebarPaneWidth
	{
		get => GetValue(SidebarPaneWidthProperty);
		set => SetValue(SidebarPaneWidthProperty, value);
	}

	private bool _sidebarInitialized;


	private async void UpdateSidebarColumn()
	{
		if (_vm is null)
			return;
		var open = _vm.IsSidebarOpen;
		var target = open ? Math.Max(160, SettingsService.Current.SidebarWidth) : 0;
		var current = ContentGrid.ColumnDefinitions[0].ActualWidth;
		if (!_sidebarInitialized || !Motion.IsEnabled || !IsVisible)
		{
			_sidebarInitialized = true;
			SidebarPaneWidth = target;
			SidebarList.IsVisible = open;
			SidebarSplitter.IsVisible = open;
			return;
		}

		// Keep the sidebar visible while it slides closed, then hide it.
		SidebarList.IsVisible = true;
		SidebarSplitter.IsVisible = false;
		if (open)
			_ = Motion.SlideInAsync(SidebarList, -24);
		await Motion.AnimateDoubleAsync(this, SidebarPaneWidthProperty, current, target, Motion.Pane, Motion.Decelerate);
		SidebarPaneWidth = target;
		if (_vm.IsSidebarOpen == open)
		{
			SidebarList.IsVisible = open;
			SidebarSplitter.IsVisible = open;
		}
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
		SuggestionPopup.IsOpen = false;
		PathBox.IsVisible = false;
		BreadcrumbHost.IsVisible = true;
		Dispatcher.UIThread.Post(() => BreadcrumbScroller.ScrollToEnd(), DispatcherPriority.Background);
	}

	private void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
	{
		var suggestions = SuggestionPopup.IsOpen ? SuggestionList.ItemCount : 0;
		switch (e.Key)
		{
			case Key.Down when suggestions > 0:
				SuggestionList.SelectedIndex = Math.Min(SuggestionList.SelectedIndex + 1, suggestions - 1);
				SuggestionList.ScrollIntoView(SuggestionList.SelectedIndex);
				e.Handled = true;
				break;
			case Key.Up when suggestions > 0:
				SuggestionList.SelectedIndex = Math.Max(SuggestionList.SelectedIndex - 1, -1);
				e.Handled = true;
				break;
			case Key.Enter:
				if (SuggestionPopup.IsOpen && SuggestionList.SelectedItem is AddressSuggestion pick)
					AcceptSuggestion(pick);
				else
					ExecuteAddress(PathBox.Text ?? string.Empty);
				e.Handled = true;
				break;
			case Key.Escape when SuggestionPopup.IsOpen:
				SuggestionPopup.IsOpen = false;
				e.Handled = true;
				break;
			case Key.Escape:
				EndEditPath();
				Shell.FocusActiveList();
				e.Handled = true;
				break;
			case Key.Tab:
				if (SuggestionPopup.IsOpen && (SuggestionList.SelectedItem ?? SuggestionList.Items.OfType<object>().FirstOrDefault()) is AddressSuggestion first)
					SetPathText(first.Text);
				else
					CompletePath();
				e.Handled = true;
				break;
		}
	}

	private bool _settingPathText;

	private void SetPathText(string text)
	{
		_settingPathText = true;
		PathBox.Text = text;
		PathBox.CaretIndex = text.Length;
		_settingPathText = false;
		UpdateSuggestions();
	}

	/// <summary>Shows matching folders, files and commands under the address bar while typing.</summary>
	private void UpdateSuggestions()
	{
		if (_settingPathText && !PathBox.IsVisible)
			return;
		var text = PathBox.Text ?? string.Empty;
		if (!PathBox.IsVisible || text.Length == 0 || text == CurrentPathText())
		{
			SuggestionPopup.IsOpen = false;
			return;
		}
		var folder = Tab is { IsFolder: true } t ? t.CurrentPath : XdgPaths.Home;
		var items = AddressBarService.Default.Suggest(text, folder, SettingsService.Current.ShowHiddenItems);
		SuggestionList.ItemsSource = items;
		SuggestionList.SelectedIndex = -1;
		SuggestionPopup.MinWidth = Omnibar.Bounds.Width;
		SuggestionPopup.IsOpen = items.Count > 0;
	}

	/// <summary>The text the address bar shows for the current location when editing starts.</summary>
	private string CurrentPathText() => Tab is null ? string.Empty : Tab.IsHome ? string.Empty : Tab.IsTrash ? TrashService.TrashUri : Tab.CurrentPath ?? string.Empty;

	/// <summary>Clicking a suggestion (or Enter on it) goes there / runs it. Tab fills it in instead.</summary>
	private void AcceptSuggestion(AddressSuggestion pick) => ExecuteAddress(pick.Text);

	/// <summary>
	/// Runs what was typed, like the Windows address bar: go to a folder, open a file, open a terminal here for
	/// "cmd"/"terminal", start a program in this folder, or run a command-line tool in a terminal.
	/// </summary>
	public void ExecuteAddress(string text)
	{
		var tab = Tab;
		if (tab is null || _vm is null)
			return;
		var folder = tab.IsFolder ? tab.CurrentPath : XdgPaths.Home;
		var action = AddressBarService.Default.Resolve(text, folder);
		LastAddressAction = action;
		try
		{
			switch (action.Kind)
			{
				case AddressActionKind.Navigate:
					EndEditPath();
					tab.NavigateTo(action.Path ?? (text.Trim().Length == 0 || text.Trim().Equals("home", StringComparison.OrdinalIgnoreCase) ? null : text));
					break;
				case AddressActionKind.OpenFile:
					EndEditPath();
					LauncherService.OpenWithDefault(action.Path!);
					break;
				case AddressActionKind.OpenTerminal:
					EndEditPath();
					LauncherService.OpenInTerminal(action.Path!);
					break;
				case AddressActionKind.Run:
					EndEditPath();
					LauncherService.Run(action.Command!, action.Path!);
					break;
				case AddressActionKind.RunInTerminal:
					EndEditPath();
					LauncherService.RunInTerminal(action.Command!, action.Path!);
					break;
				default:
					SuggestionPopup.IsOpen = false;
					_ = ShowAddressErrorAsync(text, string.Format(Loc.Get("AddressNotFound", "Files can't find \"{0}\". Check the spelling and try again."), text.Trim()));
					return;
			}
			Shell.FocusActiveList();
		}
		catch (Exception ex)
		{
			_ = ShowAddressErrorAsync(text, ex.Message);
		}
	}

	/// <summary>What the last Enter in the address bar did (used by tests).</summary>
	internal AddressAction? LastAddressAction { get; private set; }

	private async Task ShowAddressErrorAsync(string text, string message)
	{
		if (_vm is null)
			return;
		await _vm.Dialogs.ShowErrorAsync("Files", message);
		// Back to the address bar with the text, so it can be corrected.
		BeginEditPath();
		SetPathText(text);
		PathBox.SelectAll();
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
		// The chevron turns to point down while its folder menu is open.
		control.Classes.Add("open");
		menu.Closed += (_, _) => control.Classes.Remove("open");
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
			SetSidebarDropTarget(null);
			return;
		}
		e.DragEffects = item.Kind == SidebarItemKind.Trash ? DragDropEffects.Move : (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? DragDropEffects.Copy : DragDropEffects.Move);
		SetSidebarDropTarget(SidebarList.ContainerFromItem(item) as ListBoxItem);
	}

	private ListBoxItem? _sidebarDropTarget;

	/// <summary>Highlights the sidebar item files are being dragged onto.</summary>
	private void SetSidebarDropTarget(ListBoxItem? container)
	{
		if (container == _sidebarDropTarget)
			return;
		_sidebarDropTarget?.Classes.Remove("droptarget");
		_sidebarDropTarget = container;
		_sidebarDropTarget?.Classes.Add("droptarget");
	}

	private async void OnSidebarDrop(object? sender, DragEventArgs e)
	{
		SetSidebarDropTarget(null);
		var item = SidebarItemFromSource(e.Source);
		var paths = ShellView.GetDroppedPaths(e.Data);
		if (_vm is null || item?.Path is null || paths.Count == 0)
			return;
		e.Handled = true;
		if (item.Kind == SidebarItemKind.Trash)
		{
			var errors = new List<string>();
			var trashed = new List<TrashEntry>();
			await Task.Run(() =>
			{
				foreach (var p in paths)
				{
					try
					{
						trashed.Add(TrashService.Default.Trash(p));
					}
					catch (Exception ex)
					{
						errors.Add($"{Path.GetFileName(p)}: {ex.Message}");
					}
				}
			});
			if (trashed.Count > 0)
				UndoService.Record(UndoService.ForTrash(trashed));
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

	/// <summary>
	/// File shortcuts (Ctrl+C, Ctrl+V, F2, Delete...) work wherever the focus is, except while typing in a text box.
	/// Enter and Space are left to a focused button or menu, and keys that act on items (Enter, Delete, F2,
	/// Backspace) are left to the sidebar when it has focus.
	/// </summary>
	private bool CanUseFileShortcut(Key key)
	{
		var focused = FocusManager?.GetFocusedElement() as Control;
		if (focused is TextBox)
			return false;
		if (focused is Button or MenuItem or ComboBox && key is Key.Enter or Key.Space)
			return false;
		var inSidebar = focused is not null && (focused == SidebarList || focused.FindAncestorOfType<ListBox>() == SidebarList);
		if (inSidebar && key is Key.Enter or Key.Space or Key.Delete or Key.F2 or Key.Back)
			return false;
		return true;
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
			case Key.E when ctrl:
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
				SpinRefreshIcon();
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

		if (handled || tab is null || !CanUseFileShortcut(e.Key))
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
			case Key.Z when ctrl && shift:
			case Key.Y when ctrl:
				_vm.RedoCommand.Execute(null);
				break;
			case Key.Z when ctrl:
				_vm.UndoCommand.Execute(null);
				break;
			case Key.D when ctrl:
				tab.DeleteCommand.Execute(null);
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
