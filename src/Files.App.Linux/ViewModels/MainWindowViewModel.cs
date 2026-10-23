// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Files.App.Linux.Models;
using Files.App.Linux.Services;

namespace Files.App.Linux.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
	private readonly Stack<string?> _closedTabs = new();

	public MainWindowViewModel(bool watchMounts = true)
	{
		Sidebar = new SidebarViewModel(watchMounts);
		Sidebar.DrivesChanged += (_, _) =>
		{
			foreach (var tab in Tabs.Where(t => t.IsHome))
				tab.Home.Load(Sidebar);
		};
		IsSidebarOpen = SettingsService.Current.IsSidebarOpen;
		IsInfoPaneOpen = SettingsService.Current.ShowInfoPane;
		SettingsService.Changed += OnSettingsChanged;
	}

	private void OnSettingsChanged(object? sender, EventArgs e)
	{
		Sidebar.Rebuild();
		OnPinnedChanged();
		RefreshAll();
	}

	public void Detach() => SettingsService.Changed -= OnSettingsChanged;

	public IDialogService Dialogs { get; set; } = new NullDialogService();

	public ObservableCollection<ShellTabViewModel> Tabs { get; } = [];

	public SidebarViewModel Sidebar { get; }

	public OperationsViewModel Operations { get; } = new();

	[ObservableProperty]
	public partial ShellTabViewModel? SelectedTab { get; set; }

	[ObservableProperty]
	public partial bool IsSidebarOpen { get; set; }

	[ObservableProperty]
	public partial bool IsInfoPaneOpen { get; set; }

	[ObservableProperty]
	public partial string WindowTitle { get; set; } = "Files";

	partial void OnSelectedTabChanged(ShellTabViewModel? value)
	{
		if (value is null)
			return;
		Sidebar.SyncSelection(value.CurrentPath);
		WindowTitle = $"{value.Title} - Files";
		value.UpdateCanPaste();
		if (IsInfoPaneOpen)
			value.RefreshPreview();
	}

	partial void OnIsSidebarOpenChanged(bool value)
	{
		SettingsService.Current.IsSidebarOpen = value;
		SettingsService.Save();
	}

	partial void OnIsInfoPaneOpenChanged(bool value)
	{
		SettingsService.Current.ShowInfoPane = value;
		SettingsService.Save();
		if (value)
			SelectedTab?.RefreshPreview();
	}

	public void Initialize(IReadOnlyList<string> startupPaths)
	{
		if (startupPaths.Count > 0)
		{
			foreach (var p in startupPaths)
				OpenTab(p);
		}
		else if (SettingsService.Current.Startup == StartupBehavior.ContinueWhereLeftOff && SettingsService.Current.LastSessionTabs.Count > 0)
		{
			foreach (var p in SettingsService.Current.LastSessionTabs)
				OpenTab(p == "home" ? null : p);
		}
		else
		{
			OpenTab(null);
		}
		if (Tabs.Count == 0)
			OpenTab(null);
		SelectedTab = Tabs[0];
	}

	public ShellTabViewModel OpenTab(string? path, bool activate = true)
	{
		var tab = new ShellTabViewModel(this);
		var index = SelectedTab is null ? Tabs.Count : Tabs.IndexOf(SelectedTab) + 1;
		if (!activate || index <= 0 || index > Tabs.Count)
			index = Tabs.Count;
		Tabs.Insert(index, tab);
		tab.PropertyChanged += (s, e) =>
		{
			if (e.PropertyName == nameof(ShellTabViewModel.Title) && s == SelectedTab)
				WindowTitle = $"{tab.Title} - Files";
		};
		tab.NavigateTo(path, addToHistory: false);
		if (activate)
			SelectedTab = tab;
		return tab;
	}

	[RelayCommand]
	private void NewTab() => OpenTab(null);

	[RelayCommand]
	public void CloseTab(ShellTabViewModel? tab)
	{
		tab ??= SelectedTab;
		if (tab is null)
			return;
		var index = Tabs.IndexOf(tab);
		_closedTabs.Push(tab.CurrentPath);
		Tabs.Remove(tab);
		tab.Dispose();
		if (Tabs.Count == 0)
		{
			CloseRequested?.Invoke(this, EventArgs.Empty);
			return;
		}
		if (SelectedTab == tab || SelectedTab is null)
			SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
	}

	public event EventHandler? CloseRequested;

	[RelayCommand]
	private void CloseOtherTabs(ShellTabViewModel? tab)
	{
		tab ??= SelectedTab;
		foreach (var t in Tabs.Where(t => t != tab).ToList())
			CloseTab(t);
	}

	[RelayCommand]
	private void CloseTabsToTheRight(ShellTabViewModel? tab)
	{
		tab ??= SelectedTab;
		if (tab is null)
			return;
		var index = Tabs.IndexOf(tab);
		foreach (var t in Tabs.Skip(index + 1).ToList())
			CloseTab(t);
	}

	[RelayCommand]
	private void DuplicateTab(ShellTabViewModel? tab)
	{
		tab ??= SelectedTab;
		if (tab is not null)
			OpenTab(tab.CurrentPath);
	}

	[RelayCommand]
	private void ReopenClosedTab()
	{
		if (_closedTabs.Count > 0)
			OpenTab(_closedTabs.Pop());
	}

	[RelayCommand]
	private void NextTab() => SwitchTab(1);

	[RelayCommand]
	private void PreviousTab() => SwitchTab(-1);

	private void SwitchTab(int delta)
	{
		if (Tabs.Count == 0 || SelectedTab is null)
			return;
		var index = (Tabs.IndexOf(SelectedTab) + delta + Tabs.Count) % Tabs.Count;
		SelectedTab = Tabs[index];
	}

	public void SelectTabByNumber(int number)
	{
		if (Tabs.Count == 0)
			return;
		SelectedTab = number >= 9 ? Tabs[^1] : Tabs[Math.Min(number - 1, Tabs.Count - 1)];
	}

	[RelayCommand]
	private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

	[RelayCommand]
	private void ToggleInfoPane() => IsInfoPaneOpen = !IsInfoPaneOpen;

	[RelayCommand]
	private void ToggleHiddenItems()
	{
		SettingsService.Current.ShowHiddenItems = !SettingsService.Current.ShowHiddenItems;
		SettingsService.NotifyChanged();
	}

	[RelayCommand]
	private void ToggleFileExtensions()
	{
		SettingsService.Current.ShowFileExtensions = !SettingsService.Current.ShowFileExtensions;
		SettingsService.NotifyChanged();
	}

	[RelayCommand]
	private void OpenSettings() => Dialogs.ShowSettings();

	public void RefreshAll()
	{
		foreach (var tab in Tabs)
			_ = tab.LoadAsync(keepSelection: true);
	}

	public void OnSortChanged()
	{
		foreach (var tab in Tabs)
			tab.Resort();
		OnPropertyChanged(nameof(SortBy));
		OnPropertyChanged(nameof(SortDescending));
	}

	public SortColumn SortBy => SettingsService.Current.SortBy;

	public bool SortDescending => SettingsService.Current.SortDescending;

	public void OnTabNavigated(ShellTabViewModel tab)
	{
		if (tab != SelectedTab)
			return;
		Sidebar.SyncSelection(tab.CurrentPath);
		WindowTitle = $"{tab.Title} - Files";
	}

	public void OnPinnedChanged()
	{
		foreach (var tab in Tabs.Where(t => t.IsHome))
			tab.Home.Load(Sidebar);
		if (SelectedTab is not null)
			Sidebar.SyncSelection(SelectedTab.CurrentPath);
	}

	public void RefreshTrashState()
	{
		foreach (var tab in Tabs.Where(t => t.IsTrash && t != SelectedTab))
			_ = tab.LoadAsync();
	}

	public void NavigateSidebarItem(SidebarItem item, bool newTab)
	{
		if (item.IsHeader)
		{
			Sidebar.ToggleSection(item);
			return;
		}
		var path = item.Kind == SidebarItemKind.Home ? null : item.Path;
		if (newTab || SelectedTab is null)
			OpenTab(path);
		else
			SelectedTab.NavigateTo(path);
	}

	/// <summary>Copies or moves items with progress reporting and conflict prompts. Returns created paths.</summary>
	public async Task<List<string>> TransferAsync(IReadOnlyList<string> sources, string destination, bool move)
	{
		var verb = move ? Loc.Get("Move", "Move") : Loc.Get("Copy", "Copy");
		var title = sources.Count == 1 ? $"{verb} \"{Path.GetFileName(sources[0])}\"" : $"{verb} {sources.Count} items";
		var op = Operations.Start($"{title} → {Path.GetFileName(destination.TrimEnd('/')) switch { { Length: > 0 } n => n, _ => destination }}");
		var progress = new Progress<OperationProgress>(op.Report);
		try
		{
			var created = await FileOperationsService.TransferAsync(sources, destination, move, path => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Dialogs.ResolveConflictAsync(path)), progress, op.Cancellation.Token);
			Operations.Complete(op);
			return created;
		}
		catch (OperationCanceledException)
		{
			Operations.Complete(op);
			return [];
		}
		catch (Exception ex)
		{
			Operations.Complete(op, ex.Message);
			await Dialogs.ShowErrorAsync(title, ex.Message);
			return [];
		}
	}

	public void SaveSession()
	{
		SettingsService.Current.LastSessionTabs = Tabs.Select(t => t.IsSearchResults ? t.CurrentPath : t.CurrentPath).Select(p => p ?? "home").ToList();
		SettingsService.Save();
	}
}
