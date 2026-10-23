// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Files.App.Linux.Services;

public enum AppTheme
{
	System,
	Light,
	Dark,
}

public enum LayoutMode
{
	Details,
	List,
	GridSmall,
	GridMedium,
	GridLarge,
}

public enum SortColumn
{
	Name,
	DateModified,
	DateCreated,
	Type,
	Size,
}

public enum StartupBehavior
{
	OpenHome,
	ContinueWhereLeftOff,
}

public sealed class AppSettings
{
	public AppTheme Theme { get; set; } = AppTheme.System;
	public string? Language { get; set; }
	public bool ShowHiddenItems { get; set; }
	public bool ShowFileExtensions { get; set; } = true;
	public bool ShowThumbnails { get; set; } = true;
	public bool ShowDeleteConfirmation { get; set; } = true;
	public bool ShowInfoPane { get; set; }
	public bool IsSidebarOpen { get; set; } = true;
	public double SidebarWidth { get; set; } = 240;
	public double InfoPaneWidth { get; set; } = 300;
	public LayoutMode DefaultLayout { get; set; } = LayoutMode.Details;
	public SortColumn SortBy { get; set; } = SortColumn.Name;
	public bool SortDescending { get; set; }
	public bool FoldersFirst { get; set; } = true;
	public StartupBehavior Startup { get; set; } = StartupBehavior.OpenHome;
	public List<string> LastSessionTabs { get; set; } = [];
	public List<string>? PinnedFolders { get; set; }
	public string? TerminalCommand { get; set; }
	public double WindowWidth { get; set; } = 1200;
	public double WindowHeight { get; set; } = 760;
	public bool WindowMaximized { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Loads and saves settings to $XDG_CONFIG_HOME/files/settings.json.</summary>
public static class SettingsService
{
	public static AppSettings Current { get; private set; } = new();

	public static event EventHandler? Changed;

	/// <summary>Overrides the config directory (used by tests).</summary>
	public static string? ConfigDirectoryOverride { get; set; }

	public static string ConfigDirectory => ConfigDirectoryOverride ?? Path.Combine(XdgPaths.ConfigHome, "files");

	public static string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

	public static void Load()
	{
		try
		{
			if (File.Exists(SettingsPath))
			{
				var json = File.ReadAllText(SettingsPath);
				Current = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
				return;
			}
		}
		catch
		{
			// Corrupt settings: start fresh.
		}
		Current = new AppSettings();
	}

	public static void Save()
	{
		try
		{
			Directory.CreateDirectory(ConfigDirectory);
			var tmp = SettingsPath + ".tmp";
			File.WriteAllText(tmp, JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings));
			File.Move(tmp, SettingsPath, true);
		}
		catch
		{
			// Settings are best-effort.
		}
	}

	public static void NotifyChanged()
	{
		Save();
		Changed?.Invoke(null, EventArgs.Empty);
	}
}
