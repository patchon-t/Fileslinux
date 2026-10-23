// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Files.App.Linux.Models;
using Files.App.Linux.Services;
using FluentIcons.Common;

namespace Files.App.Linux.ViewModels;

public sealed record BreadcrumbItem(string Name, string? Path, bool IsLast);

/// <summary>One tab: its location, history, items, selection and the commands that act on them.</summary>
public sealed partial class ShellTabViewModel : ObservableObject, IDisposable
{
	private readonly MainWindowViewModel _main;
	private readonly Stack<string?> _back = new();
	private readonly Stack<string?> _forward = new();
	private CancellationTokenSource? _loadCts;
	private FileSystemWatcher? _watcher;
	private DispatcherTimer? _refreshDebounce;
	private List<FileItem> _allItems = [];
	private string? _pendingSelectPath;
	private bool _hasNavigated;

	public ShellTabViewModel(MainWindowViewModel main)
	{
		_main = main;
		Layout = SettingsService.Current.DefaultLayout;
		ClipboardService.Changed += OnClipboardChanged;
	}

	public MainWindowViewModel Main => _main;

	public ObservableCollection<FileItem> Items { get; } = [];

	public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = [];

	public HomeViewModel Home { get; } = new();

	public PreviewViewModel Preview { get; } = new();

	public IReadOnlyList<FileItem> SelectedItems { get; private set; } = [];

	/// <summary>Raised when the view should change the selection (select all, select new item...).</summary>
	public event EventHandler<IReadOnlyList<FileItem>>? SelectionRequested;

	/// <summary>Raised after a navigation completes so the view can move focus and scroll to top.</summary>
	public event EventHandler? Navigated;

	/// <summary>Null means Home; <see cref="TrashService.TrashUri"/> means the trash.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsHome), nameof(IsTrash), nameof(IsFolder), nameof(Title), nameof(TitleIcon), nameof(CanGoUp), nameof(CanCreateItems))]
	public partial string? CurrentPath { get; private set; }

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsDetailsLayout), nameof(IsGridLayout), nameof(IsListLayout), nameof(GridItemWidth), nameof(GridIconSize), nameof(GridItemHeight))]
	public partial LayoutMode Layout { get; set; }

	[ObservableProperty]
	public partial string SearchQuery { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Title), nameof(ShowLocationColumn), nameof(CanCreateItems))]
	public partial bool IsSearchResults { get; private set; }

	[ObservableProperty]
	public partial bool IsEmpty { get; private set; }

	[ObservableProperty]
	public partial string? ErrorMessage { get; private set; }

	[ObservableProperty]
	public partial string StatusText { get; private set; } = string.Empty;

	[ObservableProperty]
	public partial string SelectionText { get; private set; } = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
	public partial bool CanGoBack { get; private set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
	public partial bool CanGoForward { get; private set; }

	[ObservableProperty]
	public partial bool HasSelection { get; private set; }

	[ObservableProperty]
	public partial bool IsSingleSelection { get; private set; }

	[ObservableProperty]
	public partial bool CanPaste { get; private set; }

	[ObservableProperty]
	public partial bool SelectionIsArchive { get; private set; }

	[ObservableProperty]
	public partial bool SelectionIsFolder { get; private set; }

	public bool IsHome => CurrentPath is null;

	public bool IsTrash => CurrentPath == TrashService.TrashUri;

	public bool IsFolder => !IsHome && !IsTrash;

	public bool CanGoUp => IsFolder && CurrentPath != "/";

	public bool CanCreateItems => IsFolder && !IsSearchResults;

	public bool ShowLocationColumn => IsSearchResults || IsTrash;

	public bool IsDetailsLayout => Layout == LayoutMode.Details;

	public bool IsListLayout => Layout == LayoutMode.List;

	public bool IsGridLayout => Layout is LayoutMode.GridSmall or LayoutMode.GridMedium or LayoutMode.GridLarge;

	public double GridIconSize => Layout switch { LayoutMode.GridSmall => 48, LayoutMode.GridLarge => 128, _ => 80 };

	public double GridItemWidth => Layout switch { LayoutMode.GridSmall => 96, LayoutMode.GridLarge => 176, _ => 128 };

	public double GridItemHeight => GridIconSize + 52;

	public string Title
	{
		get
		{
			if (IsSearchResults)
				return Loc.Format("SearchResultsFor", "Search results for `{0}`", SearchQuery).Replace("`", "\"");
			if (IsHome)
				return Loc.Get("Home", "Home");
			if (IsTrash)
				return Loc.Get("Trash", "Trash");
			return SidebarViewModel.Describe(CurrentPath!).Name;
		}
	}

	public Symbol TitleIcon => IsHome ? Symbol.Home : IsTrash ? Symbol.Delete : (CurrentPath is { } p ? SidebarViewModel.Describe(p).Icon : Symbol.Folder);

	private IDialogService Dialogs => _main.Dialogs;

	// ---------------- Navigation ----------------

	public void NavigateTo(string? path, bool addToHistory = true)
	{
		path = NormalizePath(path);
		if (path is not null && path != TrashService.TrashUri && !Directory.Exists(path))
		{
			if (File.Exists(path))
			{
				// Navigating to a file opens its folder and selects it.
				_pendingSelectPath = path;
				path = Path.GetDirectoryName(path);
			}
			else
			{
				_ = Dialogs.ShowErrorAsync(Loc.Get("InvalidItemDialogTitle", "Location not found"), $"\"{path}\" doesn't exist.");
				return;
			}
		}

		if (addToHistory && (path != CurrentPath || IsSearchResults))
		{
			if (_hasNavigated)
				_back.Push(CurrentPath);
			_forward.Clear();
		}

		_hasNavigated = true;
		IsSearchResults = false;
		SearchQuery = string.Empty;
		var previous = CurrentPath;
		CurrentPath = path;
		if (previous != path)
			OnPropertyChanged(nameof(Title));
		UpdateHistoryState();
		UpdateBreadcrumbs();
		_ = LoadAsync();
	}

	/// <summary>Expands ~, trims trailing slashes, and resolves relative paths against the current folder.</summary>
	public string? NormalizePath(string? path)
	{
		if (path is null)
			return null;
		path = path.Trim();
		if (path.Length == 0 || path.Equals("home", StringComparison.OrdinalIgnoreCase))
			return null;
		if (path.StartsWith("trash:", StringComparison.OrdinalIgnoreCase))
			return TrashService.TrashUri;
		if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(path, UriKind.Absolute, out var uri))
			path = uri.LocalPath;
		if (path == "~")
			path = XdgPaths.Home;
		else if (path.StartsWith("~/", StringComparison.Ordinal))
			path = Path.Combine(XdgPaths.Home, path[2..]);
		if (!Path.IsPathRooted(path))
			path = Path.Combine(IsFolder ? CurrentPath! : XdgPaths.Home, path);
		path = Path.GetFullPath(path);
		return path.Length > 1 ? path.TrimEnd('/') : path;
	}

	[RelayCommand(CanExecute = nameof(CanGoBack))]
	private void GoBack()
	{
		if (_back.Count == 0)
			return;
		_forward.Push(CurrentPath);
		var target = _back.Pop();
		_pendingSelectPath = CurrentPath is { } c && c != TrashService.TrashUri ? c : null;
		NavigateTo(target, addToHistory: false);
	}

	[RelayCommand(CanExecute = nameof(CanGoForward))]
	private void GoForward()
	{
		if (_forward.Count == 0)
			return;
		_back.Push(CurrentPath);
		NavigateTo(_forward.Pop(), addToHistory: false);
	}

	[RelayCommand]
	private void GoUp()
	{
		if (!CanGoUp)
			return;
		var parent = Path.GetDirectoryName(CurrentPath!);
		_pendingSelectPath = CurrentPath;
		NavigateTo(parent ?? "/");
	}

	[RelayCommand]
	private void Refresh()
	{
		if (IsSearchResults)
		{
			_ = SearchAsync(SearchQuery);
			return;
		}
		_ = LoadAsync(keepSelection: true);
	}

	private void UpdateHistoryState()
	{
		CanGoBack = _back.Count > 0;
		CanGoForward = _forward.Count > 0;
	}

	public IReadOnlyList<string?> BackHistory => [.. _back];

	public IReadOnlyList<string?> ForwardHistory => [.. _forward];

	private void UpdateBreadcrumbs()
	{
		Breadcrumbs.Clear();
		if (IsHome)
		{
			Breadcrumbs.Add(new BreadcrumbItem(Loc.Get("Home", "Home"), null, true));
			return;
		}
		if (IsTrash)
		{
			Breadcrumbs.Add(new BreadcrumbItem(Loc.Get("Trash", "Trash"), TrashService.TrashUri, true));
			return;
		}

		var path = CurrentPath!;
		var home = XdgPaths.Home;
		var parts = new List<BreadcrumbItem>();
		string acc;
		string rest;
		if (path == home || path.StartsWith(home + "/", StringComparison.Ordinal))
		{
			parts.Add(new BreadcrumbItem(Path.GetFileName(home), home, false));
			acc = home;
			rest = path[home.Length..];
		}
		else
		{
			parts.Add(new BreadcrumbItem(Loc.Get("FileSystem", "File System"), "/", false));
			acc = "/";
			rest = path;
		}

		foreach (var segment in rest.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			acc = Path.Combine(acc, segment);
			parts.Add(new BreadcrumbItem(segment, acc, false));
		}

		for (int i = 0; i < parts.Count; i++)
			Breadcrumbs.Add(parts[i] with { IsLast = i == parts.Count - 1 });
	}

	// ---------------- Loading ----------------

	public async Task LoadAsync(bool keepSelection = false)
	{
		_loadCts?.Cancel();
		var cts = new CancellationTokenSource();
		_loadCts = cts;
		var ct = cts.Token;

		var selectedPaths = keepSelection ? SelectedItems.Select(i => i.FullPath).ToHashSet() : [];
		StopWatching();
		ErrorMessage = null;

		if (IsHome)
		{
			Items.Clear();
			_allItems = [];
			Home.Load(_main.Sidebar);
			IsEmpty = false;
			IsLoading = false;
			UpdateStatus();
			_main.OnTabNavigated(this);
			Navigated?.Invoke(this, EventArgs.Empty);
			return;
		}

		IsLoading = true;
		List<FileItem> items;
		try
		{
			var path = CurrentPath!;
			var showHidden = SettingsService.Current.ShowHiddenItems;
			items = await Task.Run(() => IsTrash
				? TrashService.Default.List().Select(e => FileItem.FromInfo(Directory.Exists(e.TrashedPath) ? new DirectoryInfo(e.TrashedPath) : new FileInfo(e.TrashedPath), e)).ToList()
				: FileSystemService.Enumerate(path, showHidden, ct), ct);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception ex)
		{
			if (ct.IsCancellationRequested)
				return;
			items = [];
			ErrorMessage = ex is UnauthorizedAccessException ? $"You don't have permission to access \"{CurrentPath}\"." : ex.Message;
		}

		if (ct.IsCancellationRequested)
			return;

		ApplyItems(items, keepSelection ? selectedPaths : null);
		IsLoading = false;
		if (IsFolder)
			StartWatching(CurrentPath!);
		_main.OnTabNavigated(this);
		Navigated?.Invoke(this, EventArgs.Empty);
	}

	private void ApplyItems(List<FileItem> items, HashSet<string>? reselect)
	{
		var comparer = FileSystemService.GetComparer(SettingsService.Current.SortBy, SettingsService.Current.SortDescending, SettingsService.Current.FoldersFirst);
		items.Sort(comparer);
		_allItems = items;

		if (ClipboardService.IsCut)
		{
			var cut = ClipboardService.Paths.ToHashSet();
			foreach (var i in items)
				i.IsCut = cut.Contains(i.FullPath);
		}

		Items.Clear();
		foreach (var i in items)
			Items.Add(i);

		IsEmpty = Items.Count == 0 && ErrorMessage is null;
		UpdateStatus();

		ThumbnailService.QueueThumbnails(items.Take(2000), (int)Math.Max(96, GridIconSize * 2), _loadCts?.Token ?? default);

		var toSelect = new List<FileItem>();
		if (_pendingSelectPath is { } pending)
		{
			if (items.FirstOrDefault(i => i.FullPath == pending) is { } match)
				toSelect.Add(match);
			_pendingSelectPath = null;
		}
		else if (reselect is { Count: > 0 })
		{
			toSelect.AddRange(items.Where(i => reselect.Contains(i.FullPath)));
		}
		if (toSelect.Count > 0)
			Dispatcher.UIThread.Post(() => SelectionRequested?.Invoke(this, toSelect), DispatcherPriority.Background);
	}

	public void Resort()
	{
		var items = _allItems.ToList();
		var selected = SelectedItems.Select(i => i.FullPath).ToHashSet();
		ApplyItems(items, selected);
	}

	private void StartWatching(string path)
	{
		try
		{
			_watcher = new FileSystemWatcher(path)
			{
				IncludeSubdirectories = false,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
			};
			_watcher.Created += OnFolderChanged;
			_watcher.Deleted += OnFolderChanged;
			_watcher.Renamed += OnFolderChanged;
			_watcher.Changed += OnFolderChanged;
			_watcher.EnableRaisingEvents = true;
		}
		catch
		{
			_watcher = null;
		}
	}

	private void StopWatching()
	{
		_watcher?.Dispose();
		_watcher = null;
	}

	private void OnFolderChanged(object sender, FileSystemEventArgs e)
	{
		Dispatcher.UIThread.Post(() =>
		{
			_refreshDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
			{
				_refreshDebounce?.Stop();
				if (!IsSearchResults)
					_ = LoadAsync(keepSelection: true);
			});
			_refreshDebounce.Stop();
			_refreshDebounce.Start();
		});
	}

	// ---------------- Search ----------------

	[RelayCommand]
	public async Task SearchAsync(string? query)
	{
		query = query?.Trim();
		if (string.IsNullOrEmpty(query))
		{
			if (IsSearchResults)
				NavigateTo(CurrentPath, addToHistory: false);
			return;
		}

		var root = IsFolder ? CurrentPath! : XdgPaths.Home;
		if (!IsFolder)
		{
			_back.Push(CurrentPath);
			CurrentPath = root;
			UpdateBreadcrumbs();
			UpdateHistoryState();
		}

		_loadCts?.Cancel();
		var cts = new CancellationTokenSource();
		_loadCts = cts;
		StopWatching();

		SearchQuery = query;
		IsSearchResults = true;
		ErrorMessage = null;
		Items.Clear();
		_allItems = [];
		IsLoading = true;
		IsEmpty = false;
		UpdateStatus();

		var buffer = new List<FileItem>();
		var last = Environment.TickCount64;
		try
		{
			await foreach (var item in FileSystemService.SearchAsync(root, query, SettingsService.Current.ShowHiddenItems, cts.Token))
			{
				buffer.Add(item);
				if (Environment.TickCount64 - last > 150)
				{
					Flush();
					last = Environment.TickCount64;
				}
			}
			Flush();
		}
		catch (OperationCanceledException)
		{
			return;
		}

		IsLoading = false;
		IsEmpty = Items.Count == 0;
		UpdateStatus();
		_main.OnTabNavigated(this);

		void Flush()
		{
			foreach (var i in buffer)
			{
				Items.Add(i);
				_allItems.Add(i);
			}
			ThumbnailService.QueueThumbnails(buffer, (int)GridIconSize * 2, cts.Token);
			buffer.Clear();
			UpdateStatus();
		}
	}

	// ---------------- Selection ----------------

	public void SetSelection(IReadOnlyList<FileItem> items)
	{
		SelectedItems = items;
		HasSelection = items.Count > 0;
		IsSingleSelection = items.Count == 1;
		SelectionIsArchive = items.Count == 1 && !items[0].IsDirectory && FileTypes.IsArchive(items[0].Name);
		SelectionIsFolder = items.Count == 1 && items[0].IsDirectory;
		UpdateStatus();
		if (_main.IsInfoPaneOpen)
			Preview.Update(items);
	}

	public void RefreshPreview() => Preview.Update(SelectedItems);

	[RelayCommand]
	private void SelectAll() => SelectionRequested?.Invoke(this, Items.ToList());

	[RelayCommand]
	private void InvertSelection()
	{
		var selected = SelectedItems.ToHashSet();
		SelectionRequested?.Invoke(this, Items.Where(i => !selected.Contains(i)).ToList());
	}

	[RelayCommand]
	private void ClearSelection() => SelectionRequested?.Invoke(this, []);

	private void UpdateStatus()
	{
		if (IsHome)
		{
			StatusText = string.Empty;
			SelectionText = string.Empty;
			return;
		}
		StatusText = Items.Count == 1 ? Loc.Format("GroupItemsCount_Singular", "{0} item", 1) : Loc.Format("GroupItemsCount_Plural", "{0} items", Items.Count);
		if (SelectedItems.Count == 0)
		{
			SelectionText = string.Empty;
			return;
		}
		var text = Loc.Format("SelectedItems", "{0, plural, one {# item selected} other {# items selected}}", SelectedItems.Count);
		var files = SelectedItems.Where(i => !i.IsDirectory).ToList();
		if (files.Count > 0)
			text += "  " + FormatHelpers.FormatSize(files.Sum(f => f.Size));
		SelectionText = text;
	}

	private void OnClipboardChanged(object? sender, EventArgs e)
	{
		CanPaste = ClipboardService.HasItems;
		var cut = ClipboardService.IsCut ? ClipboardService.Paths.ToHashSet() : [];
		foreach (var i in Items)
			i.IsCut = cut.Contains(i.FullPath);
	}

	public void UpdateCanPaste() => CanPaste = ClipboardService.HasItems;

	// ---------------- Item commands ----------------

	[RelayCommand]
	public void OpenItem(FileItem? item)
	{
		item ??= SelectedItems.FirstOrDefault();
		if (item is null)
			return;

		if (item.TrashEntry is not null)
		{
			_ = Dialogs.ShowErrorAsync(Loc.Get("Trash", "Trash"), "Restore this item to open it.");
			return;
		}

		if (item.IsDirectory)
		{
			NavigateTo(item.FullPath);
			return;
		}

		try
		{
			LauncherService.OpenWithDefault(item.FullPath);
		}
		catch (Exception ex)
		{
			_ = Dialogs.ShowErrorAsync(Loc.Get("Open", "Open"), ex.Message);
		}
	}

	[RelayCommand]
	private void OpenSelected()
	{
		var selection = SelectedItems.ToList();
		if (selection.Count == 1)
		{
			OpenItem(selection[0]);
			return;
		}
		foreach (var item in selection)
		{
			if (item.IsDirectory)
				_main.OpenTab(item.FullPath);
			else
				OpenItem(item);
		}
	}

	[RelayCommand]
	private void OpenInNewTab(FileItem? item)
	{
		var targets = item is not null ? [item] : SelectedItems.Where(i => i.IsDirectory).ToList();
		foreach (var t in targets.Where(t => t.IsDirectory))
			_main.OpenTab(t.FullPath, activate: false);
	}

	[RelayCommand]
	private void OpenFileLocation(FileItem? item)
	{
		item ??= SelectedItems.FirstOrDefault();
		if (item?.ParentPath is { } parent)
		{
			_pendingSelectPath = item.FullPath;
			NavigateTo(parent);
		}
	}

	[RelayCommand]
	private async Task CopyAsync() => await SetClipboardAsync(cut: false);

	[RelayCommand]
	private async Task CutAsync() => await SetClipboardAsync(cut: true);

	private async Task SetClipboardAsync(bool cut)
	{
		if (!HasSelection || IsTrash)
			return;
		await ClipboardService.SetAsync(SelectedItems.Select(i => i.FullPath).ToList(), cut);
	}

	[RelayCommand]
	private async Task PasteAsync()
	{
		if (!IsFolder || IsSearchResults)
			return;
		var (paths, cut) = await ClipboardService.GetAsync();
		if (paths.Count == 0)
			return;

		var created = await _main.TransferAsync(paths, CurrentPath!, cut);
		if (cut && created.Count > 0)
			ClipboardService.Clear();
		SelectAfterRefresh(created);
	}

	private void SelectAfterRefresh(IReadOnlyCollection<string> paths)
	{
		if (paths.Count == 0)
			return;
		_ = LoadAsync().ContinueWith(_ => Dispatcher.UIThread.Post(() =>
		{
			var set = paths.ToHashSet();
			SelectionRequested?.Invoke(this, Items.Where(i => set.Contains(i.FullPath)).ToList());
		}), TaskScheduler.Default);
	}

	[RelayCommand]
	private async Task CopyPathAsync()
	{
		var text = HasSelection ? string.Join("\n", SelectedItems.Select(i => i.FullPath)) : CurrentPath ?? XdgPaths.Home;
		await ClipboardService.SetTextAsync(text);
	}

	[RelayCommand]
	private async Task RenameAsync()
	{
		if (!IsSingleSelection || IsTrash)
			return;
		var item = SelectedItems[0];
		var name = await Dialogs.PromptAsync(Loc.Get("Rename", "Rename"), null, item.Name, Loc.Get("Rename", "Rename"));
		if (string.IsNullOrWhiteSpace(name) || name == item.Name)
			return;

		if (!item.IsDirectory && !string.Equals(Path.GetExtension(name), item.Extension, StringComparison.OrdinalIgnoreCase))
		{
			var (ok, _) = await Dialogs.ConfirmAsync(Loc.Get("Rename", "Rename"), Loc.Get("RenameFileDialog.Text", "If you change a file extension, the file might become unusable. Are you sure you want to change it?"), Loc.Get("Yes", "Yes"));
			if (!ok)
				return;
		}

		try
		{
			var target = FileOperationsService.Rename(item.FullPath, name);
			_pendingSelectPath = target;
			await LoadAsync();
		}
		catch (Exception ex)
		{
			await Dialogs.ShowErrorAsync(Loc.Get("Rename", "Rename"), ex.Message);
		}
	}

	[RelayCommand]
	private async Task DeleteAsync() => await DeleteCoreAsync(permanent: false);

	[RelayCommand]
	private async Task DeletePermanentlyAsync() => await DeleteCoreAsync(permanent: true);

	private async Task DeleteCoreAsync(bool permanent)
	{
		if (!HasSelection)
			return;
		var items = SelectedItems.ToList();
		permanent |= IsTrash;

		if (SettingsService.Current.ShowDeleteConfirmation || permanent)
		{
			var title = Loc.Format("DeleteItemsDialogTitle", "Delete {0, plural, one {item} other {items}}", items.Count);
			var message = items.Count == 1
				? (permanent ? $"Are you sure you want to permanently delete \"{items[0].Name}\"?" : $"Are you sure you want to move \"{items[0].Name}\" to the trash?")
				: (permanent ? $"Are you sure you want to permanently delete these {items.Count} items?" : $"Are you sure you want to move these {items.Count} items to the trash?");
			var (ok, perm) = await Dialogs.ConfirmAsync(title, message, Loc.Get("Delete", "Delete"), IsTrash ? null : Loc.Get("DeleteItemsDialogPermanentlyDeleteCheckBox.Content", "Permanently delete"), permanent, isDestructive: true);
			if (!ok)
				return;
			permanent |= perm;
		}

		var op = _main.Operations.Start(Loc.Format("DeleteItemsDialogTitle", "Delete {0, plural, one {item} other {items}}", items.Count));
		var errors = new List<string>();
		await Task.Run(() =>
		{
			foreach (var item in items)
			{
				try
				{
					if (item.TrashEntry is { } entry)
						TrashService.Default.DeletePermanently(entry);
					else if (permanent)
						FileOperationsService.DeletePermanently(item.FullPath);
					else
						TrashService.Default.Trash(item.FullPath);
				}
				catch (Exception ex)
				{
					errors.Add($"{item.Name}: {ex.Message}");
				}
			}
		});
		_main.Operations.Complete(op, errors.Count > 0 ? string.Join("\n", errors) : null);

		if (errors.Count > 0 && !permanent)
		{
			var (ok, _) = await Dialogs.ConfirmAsync(Loc.Get("Delete", "Delete"), "Some items couldn't be moved to the trash:\n" + string.Join("\n", errors.Take(5)) + "\n\nDelete them permanently instead?", Loc.Get("DeletePermanently", "Delete permanently"), isDestructive: true);
			if (ok)
			{
				foreach (var item in items.Where(i => FileOperationsService.Exists(i.FullPath)))
				{
					try
					{
						FileOperationsService.DeletePermanently(item.FullPath);
					}
					catch (Exception ex)
					{
						await Dialogs.ShowErrorAsync(Loc.Get("Delete", "Delete"), ex.Message);
						break;
					}
				}
			}
		}
		else if (errors.Count > 0)
		{
			await Dialogs.ShowErrorAsync(Loc.Get("Delete", "Delete"), string.Join("\n", errors.Take(10)));
		}

		await LoadAsync();
		_main.RefreshTrashState();
	}

	[RelayCommand]
	private async Task NewFolderAsync()
	{
		if (!CanCreateItems)
			return;
		var name = await Dialogs.PromptAsync(Loc.Get("NewFolder", "New folder"), null, UniqueChildName(Loc.Get("NewFolder", "New folder")), Loc.Get("Create", "Create"), selectNameWithoutExtension: false);
		if (string.IsNullOrWhiteSpace(name))
			return;
		try
		{
			_pendingSelectPath = FileOperationsService.CreateFolder(CurrentPath!, name.Trim());
			await LoadAsync();
		}
		catch (Exception ex)
		{
			await Dialogs.ShowErrorAsync(Loc.Get("NewFolder", "New folder"), ex.Message);
		}
	}

	[RelayCommand]
	private async Task NewFileAsync()
	{
		if (!CanCreateItems)
			return;
		var name = await Dialogs.PromptAsync(Loc.Get("NewFile", "New file"), null, UniqueChildName(Loc.Get("NewFile", "New file") + ".txt"), Loc.Get("Create", "Create"));
		if (string.IsNullOrWhiteSpace(name))
			return;
		try
		{
			_pendingSelectPath = FileOperationsService.CreateFile(CurrentPath!, name.Trim());
			await LoadAsync();
		}
		catch (Exception ex)
		{
			await Dialogs.ShowErrorAsync(Loc.Get("NewFile", "New file"), ex.Message);
		}
	}

	private string UniqueChildName(string name) => Path.GetFileName(FileOperationsService.GetUniqueName(Path.Combine(CurrentPath!, name)));

	[RelayCommand]
	private void OpenInTerminal()
	{
		var folder = SelectionIsFolder ? SelectedItems[0].FullPath : (IsFolder ? CurrentPath! : XdgPaths.Home);
		try
		{
			LauncherService.OpenInTerminal(folder);
		}
		catch (Exception ex)
		{
			_ = Dialogs.ShowErrorAsync(Loc.Get("OpenInTerminal", "Open in Terminal"), ex.Message);
		}
	}

	[RelayCommand]
	private void PinToSidebar()
	{
		var targets = HasSelection ? SelectedItems.Where(i => i.IsDirectory).Select(i => i.FullPath).ToList() : (IsFolder ? [CurrentPath!] : []);
		foreach (var t in targets)
			_main.Sidebar.Pin(t);
		_main.OnPinnedChanged();
	}

	[RelayCommand]
	private void UnpinFromSidebar()
	{
		var targets = HasSelection ? SelectedItems.Where(i => i.IsDirectory).Select(i => i.FullPath).ToList() : (IsFolder ? [CurrentPath!] : []);
		foreach (var t in targets)
			_main.Sidebar.Unpin(t);
		_main.OnPinnedChanged();
	}

	public bool IsSelectionPinned => SelectedItems.Count > 0 && SelectedItems.All(i => i.IsDirectory && _main.Sidebar.IsPinned(i.FullPath));

	[RelayCommand]
	private void ShowProperties()
	{
		var paths = HasSelection
			? SelectedItems.Select(i => i.FullPath).ToList()
			: (IsFolder ? [CurrentPath!] : []);
		if (paths.Count > 0)
			Dialogs.ShowProperties(paths);
	}

	[RelayCommand]
	private async Task CompressAsync()
	{
		if (!HasSelection || !IsFolder)
			return;
		var items = SelectedItems.ToList();
		var baseName = items.Count == 1 ? Path.GetFileNameWithoutExtension(items[0].Name) : Path.GetFileName(CurrentPath!) is { Length: > 0 } n ? n : "Archive";
		var op = _main.Operations.Start(Loc.Get("Compress", "Compress") + ": " + baseName + ".zip");
		try
		{
			var target = await FileOperationsService.CompressToZipAsync(items.Select(i => i.FullPath).ToList(), Path.Combine(items[0].ParentPath ?? CurrentPath!, baseName + ".zip"), op.Cancellation.Token);
			_main.Operations.Complete(op);
			_pendingSelectPath = target;
			await LoadAsync();
		}
		catch (Exception ex)
		{
			_main.Operations.Complete(op, ex.Message);
		}
	}

	[RelayCommand]
	private async Task ExtractAsync()
	{
		if (!SelectionIsArchive)
			return;
		var item = SelectedItems[0];
		var op = _main.Operations.Start(Loc.Get("Extract", "Extract") + ": " + item.Name);
		try
		{
			var dest = await FileOperationsService.ExtractAsync(item.FullPath, null, op.Cancellation.Token);
			_main.Operations.Complete(op);
			_pendingSelectPath = dest;
			await LoadAsync();
		}
		catch (Exception ex)
		{
			_main.Operations.Complete(op, ex.Message);
		}
	}

	[RelayCommand]
	private async Task RestoreAsync()
	{
		var entries = SelectedItems.Where(i => i.TrashEntry is not null).Select(i => i.TrashEntry!).ToList();
		await RestoreEntriesAsync(entries);
	}

	[RelayCommand]
	private async Task RestoreAllAsync()
	{
		var (ok, _) = await Dialogs.ConfirmAsync(Loc.Get("RestoreAllItems", "Restore All Items"), Loc.Get("ConfirmRestoreBinDialogContent", "Are you sure you want to restore all items in the trash?").Replace("recycle bin", "trash"), Loc.Get("Restore", "Restore"));
		if (ok)
			await RestoreEntriesAsync(TrashService.Default.List());
	}

	private async Task RestoreEntriesAsync(IReadOnlyList<TrashEntry> entries)
	{
		var errors = new List<string>();
		await Task.Run(() =>
		{
			foreach (var e in entries)
			{
				try
				{
					TrashService.Default.Restore(e);
				}
				catch (Exception ex)
				{
					errors.Add($"{Path.GetFileName(e.OriginalPath)}: {ex.Message}");
				}
			}
		});
		if (errors.Count > 0)
			await Dialogs.ShowErrorAsync(Loc.Get("Restore", "Restore"), string.Join("\n", errors.Take(10)));
		await LoadAsync();
		_main.RefreshTrashState();
	}

	[RelayCommand]
	private async Task EmptyTrashAsync()
	{
		var (ok, _) = await Dialogs.ConfirmAsync(Loc.Get("EmptyTrash", "Empty trash"), Loc.Get("ConfirmEmptyBinDialogContent", "Are you sure you want to permanently delete all these items?"), Loc.Get("Yes", "Yes"), isDestructive: true);
		if (!ok)
			return;
		var op = _main.Operations.Start(Loc.Get("EmptyTrash", "Empty trash"));
		await Task.Run(TrashService.Default.Empty);
		_main.Operations.Complete(op);
		if (IsTrash)
			await LoadAsync();
		_main.RefreshTrashState();
	}

	[RelayCommand]
	private void SetLayout(LayoutMode mode)
	{
		Layout = mode;
		SettingsService.Current.DefaultLayout = mode;
		SettingsService.Save();
		ThumbnailService.QueueThumbnails(Items.Where(i => i.Thumbnail is null), (int)GridIconSize * 2, _loadCts?.Token ?? default);
	}

	[RelayCommand]
	private void SetSort(SortColumn column)
	{
		if (SettingsService.Current.SortBy == column)
			SettingsService.Current.SortDescending = !SettingsService.Current.SortDescending;
		else
		{
			SettingsService.Current.SortBy = column;
			SettingsService.Current.SortDescending = false;
		}
		SettingsService.Save();
		_main.OnSortChanged();
	}

	[RelayCommand]
	private void SetSortDirection(bool descending)
	{
		SettingsService.Current.SortDescending = descending;
		SettingsService.Save();
		_main.OnSortChanged();
	}

	/// <summary>Handles files dropped onto the layout or onto a folder item.</summary>
	public async Task DropAsync(IReadOnlyList<string> paths, string? targetFolder, bool copy)
	{
		var dest = targetFolder ?? (IsFolder && !IsSearchResults ? CurrentPath : null);
		if (dest is null || paths.Count == 0)
			return;
		if (paths.All(p => Path.GetDirectoryName(p) == dest))
			return;

		// Default: move within the same drive, copy across drives.
		bool move = !copy && paths.All(p => MountService.GetMountPoint(p) == MountService.GetMountPoint(dest));
		var created = await _main.TransferAsync(paths, dest, move);
		if (dest == CurrentPath)
			SelectAfterRefresh(created);
	}

	public void Dispose()
	{
		_loadCts?.Cancel();
		StopWatching();
		_refreshDebounce?.Stop();
		ClipboardService.Changed -= OnClipboardChanged;
	}
}
