// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Linux.Services;

/// <summary>Layout and sort chosen for one folder. Null values follow the defaults.</summary>
public sealed class FolderViewSettings
{
	public LayoutMode? Layout { get; set; }
	public SortColumn? SortBy { get; set; }
	public bool? SortDescending { get; set; }

	[System.Text.Json.Serialization.JsonIgnore]
	public bool IsEmpty => Layout is null && SortBy is null && SortDescending is null;
}

/// <summary>The view a folder is shown with.</summary>
public readonly record struct FolderView(LayoutMode Layout, SortColumn SortBy, bool SortDescending, bool IsCustom);

/// <summary>
/// Remembers the view per folder, like Files on Windows: a folder opens with the default layout and sort
/// (Settings) until you change them there; the change then applies to that folder only.
/// </summary>
public static class FolderViewService
{
	private const int MaxFolders = 1000;

	public const string HomeKey = "home";

	public static string KeyFor(string? path) => string.IsNullOrEmpty(path) ? HomeKey : path;

	public static FolderView Get(string? path)
	{
		var s = SettingsService.Current;
		var custom = s.FolderViews.TryGetValue(KeyFor(path), out var v) ? v : null;
		return new FolderView(
			custom?.Layout ?? s.DefaultLayout,
			custom?.SortBy ?? s.SortBy,
			custom?.SortDescending ?? s.SortDescending,
			custom is { IsEmpty: false });
	}

	public static void SetLayout(string? path, LayoutMode layout) => Update(path, v => v.Layout = layout);

	public static void SetSort(string? path, SortColumn column, bool descending) => Update(path, v =>
	{
		v.SortBy = column;
		v.SortDescending = descending;
	});

	/// <summary>Makes the folder follow the default view again.</summary>
	public static void Reset(string? path)
	{
		if (SettingsService.Current.FolderViews.Remove(KeyFor(path)))
			SettingsService.Save();
	}

	public static void ResetAll()
	{
		SettingsService.Current.FolderViews.Clear();
		SettingsService.Save();
	}

	private static void Update(string? path, Action<FolderViewSettings> change)
	{
		var views = SettingsService.Current.FolderViews;
		var key = KeyFor(path);
		if (!views.TryGetValue(key, out var v))
		{
			v = new FolderViewSettings();
			// Keep the list bounded; drop the oldest entries first.
			while (views.Count >= MaxFolders)
				views.Remove(views.Keys.First());
			views[key] = v;
		}
		change(v);
		SettingsService.Save();
	}
}
