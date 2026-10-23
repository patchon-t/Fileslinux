// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Files.App.Linux.Models;
using Files.App.Linux.Services;

namespace Files.App.Linux.ViewModels;

public sealed partial class PropertiesViewModel : ObservableObject
{
	private readonly CancellationTokenSource _cts = new();
	private readonly IReadOnlyList<string> _paths;

	public PropertiesViewModel(IReadOnlyList<string> paths)
	{
		_paths = paths;
		IsSingle = paths.Count == 1;
		Item = IsSingle ? SafeItem(paths[0]) : null;
		Name = IsSingle ? (Item?.Name ?? Path.GetFileName(paths[0])) : Loc.Format("SelectedItems", "{0, plural, one {# item selected} other {# items selected}}", paths.Count);
		if (IsSingle && paths[0] == "/")
			Name = "/";
		TypeText = IsSingle ? (Item?.TypeDescription ?? string.Empty) : Loc.Get("MultipleItems", "Multiple items");
		Location = Path.GetDirectoryName(paths[0]) ?? "/";
		if (Item is { } i)
		{
			Modified = i.ModifiedText;
			Created = i.CreatedText;
			try
			{
				Accessed = FormatHelpers.FormatDate(File.GetLastAccessTime(i.FullPath));
			}
			catch
			{
				Accessed = string.Empty;
			}
			if (i.IsSymlink)
				LinkTarget = new FileInfo(i.FullPath).LinkTarget;
			LoadPermissions(i.FullPath);
		}
		SizeText = Loc.Get("Calculating", "Calculating...");
		_ = CalculateSizeAsync();
	}

	private static FileItem? SafeItem(string path)
	{
		try
		{
			return FileItem.FromPath(path);
		}
		catch
		{
			return null;
		}
	}

	public FileItem? Item { get; }

	public bool IsSingle { get; }

	public string Name { get; }

	public string TypeText { get; }

	public string Location { get; }

	public string Modified { get; } = string.Empty;

	public string Created { get; } = string.Empty;

	public string Accessed { get; } = string.Empty;

	public string? LinkTarget { get; }

	public bool HasLinkTarget => LinkTarget is not null;

	public bool HasDates => IsSingle;

	public string Title => $"{Name} - {Loc.Get("Properties", "Properties")}";

	[ObservableProperty]
	public partial string SizeText { get; set; }

	[ObservableProperty]
	public partial string ContainsText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool HasContains { get; set; }

	[ObservableProperty]
	public partial string Owner { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Group { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool HasPermissions { get; set; }

	[ObservableProperty]
	public partial string ModeText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string? PermissionError { get; set; }

	[ObservableProperty] public partial bool OwnerRead { get; set; }
	[ObservableProperty] public partial bool OwnerWrite { get; set; }
	[ObservableProperty] public partial bool OwnerExecute { get; set; }
	[ObservableProperty] public partial bool GroupRead { get; set; }
	[ObservableProperty] public partial bool GroupWrite { get; set; }
	[ObservableProperty] public partial bool GroupExecute { get; set; }
	[ObservableProperty] public partial bool OthersRead { get; set; }
	[ObservableProperty] public partial bool OthersWrite { get; set; }
	[ObservableProperty] public partial bool OthersExecute { get; set; }

	private async Task CalculateSizeAsync()
	{
		try
		{
			var (bytes, files, folders) = await Task.Run(() => FileOperationsService.Measure(_paths, _cts.Token), _cts.Token);
			SizeText = FormatHelpers.FormatSizeWithBytes(bytes);
			var countFolders = folders - _paths.Count(Directory.Exists);
			if (_paths.Any(Directory.Exists) || _paths.Count > 1)
			{
				ContainsText = $"{files:N0} files, {Math.Max(0, countFolders):N0} folders";
				HasContains = true;
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			SizeText = ex.Message;
		}
	}

	private void LoadPermissions(string path)
	{
		try
		{
			var mode = File.GetUnixFileMode(path);
			OwnerRead = mode.HasFlag(UnixFileMode.UserRead);
			OwnerWrite = mode.HasFlag(UnixFileMode.UserWrite);
			OwnerExecute = mode.HasFlag(UnixFileMode.UserExecute);
			GroupRead = mode.HasFlag(UnixFileMode.GroupRead);
			GroupWrite = mode.HasFlag(UnixFileMode.GroupWrite);
			GroupExecute = mode.HasFlag(UnixFileMode.GroupExecute);
			OthersRead = mode.HasFlag(UnixFileMode.OtherRead);
			OthersWrite = mode.HasFlag(UnixFileMode.OtherWrite);
			OthersExecute = mode.HasFlag(UnixFileMode.OtherExecute);
			ModeText = Convert.ToString((int)mode & 0xFFF, 8).PadLeft(4, '0');
			var (owner, group) = LauncherService.GetOwner(path);
			Owner = owner ?? string.Empty;
			Group = group ?? string.Empty;
			HasPermissions = true;
		}
		catch
		{
			HasPermissions = false;
		}
	}

	private UnixFileMode BuildMode()
	{
		var current = File.GetUnixFileMode(_paths[0]);
		var mode = current & (UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit);
		if (OwnerRead) mode |= UnixFileMode.UserRead;
		if (OwnerWrite) mode |= UnixFileMode.UserWrite;
		if (OwnerExecute) mode |= UnixFileMode.UserExecute;
		if (GroupRead) mode |= UnixFileMode.GroupRead;
		if (GroupWrite) mode |= UnixFileMode.GroupWrite;
		if (GroupExecute) mode |= UnixFileMode.GroupExecute;
		if (OthersRead) mode |= UnixFileMode.OtherRead;
		if (OthersWrite) mode |= UnixFileMode.OtherWrite;
		if (OthersExecute) mode |= UnixFileMode.OtherExecute;
		return mode;
	}

	[RelayCommand]
	private void ApplyPermissions()
	{
		if (!IsSingle)
			return;
		try
		{
			var mode = BuildMode();
			File.SetUnixFileMode(_paths[0], mode);
			ModeText = Convert.ToString((int)mode & 0xFFF, 8).PadLeft(4, '0');
			PermissionError = null;
		}
		catch (Exception ex)
		{
			PermissionError = ex.Message;
		}
	}

	public void Cancel() => _cts.Cancel();
}
