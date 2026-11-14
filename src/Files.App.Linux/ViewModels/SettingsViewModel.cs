// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Files.App.Linux.Services;

namespace Files.App.Linux.ViewModels;

public sealed record LanguageOption(string? Code, string Name)
{
	public override string ToString() => Name;
}

public sealed record EnumOption<T>(T Value, string Name)
{
	public override string ToString() => Name;
}

public sealed partial class SettingsViewModel : ObservableObject
{
	private readonly AppSettings _s = SettingsService.Current;

	public SettingsViewModel()
	{
		Themes =
		[
			new(AppTheme.System, Loc.Get("DefaultTheme", "System theme")),
			new(AppTheme.Light, Loc.Get("LightTheme", "Light")),
			new(AppTheme.Dark, Loc.Get("DarkTheme", "Dark")),
		];
		StartupOptions =
		[
			new(StartupBehavior.OpenHome, Loc.Get("SettingsOnStartupOpenANewTab.Content", "Open a new tab")),
			new(StartupBehavior.ContinueWhereLeftOff, Loc.Get("SettingsOnStartupContinueWhereYouLeftOff.Content", "Continue where you left off")),
		];
		Layouts =
		[
			new(LayoutMode.Details, Loc.Get("Details", "Details")),
			new(LayoutMode.List, Loc.Get("List", "List")),
			new(LayoutMode.GridSmall, Loc.Get("SmallIcons", "Small icons")),
			new(LayoutMode.GridMedium, Loc.Get("MediumIcons", "Medium icons")),
			new(LayoutMode.GridLarge, Loc.Get("LargeIcons", "Large icons")),
		];

		Languages = [new LanguageOption(null, Loc.Get("SettingsDefaultLanguage", "Default (system)"))];
		foreach (var code in Loc.AvailableLanguages())
		{
			string name;
			try
			{
				name = CultureInfo.GetCultureInfo(code).NativeName;
			}
			catch
			{
				name = code;
			}
			Languages.Add(new LanguageOption(code, name));
		}

		SelectedTheme = Themes.First(t => t.Value == _s.Theme);
		SelectedStartup = StartupOptions.First(t => t.Value == _s.Startup);
		SelectedLayout = Layouts.First(t => t.Value == _s.DefaultLayout);
		SelectedLanguage = Languages.FirstOrDefault(l => l.Code == _s.Language) ?? Languages[0];
		ShowHiddenItems = _s.ShowHiddenItems;
		ShowFileExtensions = _s.ShowFileExtensions;
		ShowThumbnails = _s.ShowThumbnails;
		ShowDeleteConfirmation = _s.ShowDeleteConfirmation;
		FoldersFirst = _s.FoldersFirst;
		EnableAnimations = _s.EnableAnimations;
		LoadIconStyles();
		UseSystemTitleBar = _s.UseSystemTitleBar;
		TerminalCommand = _s.TerminalCommand ?? string.Empty;
	}

	public List<EnumOption<AppTheme>> Themes { get; }

	public List<EnumOption<StartupBehavior>> StartupOptions { get; }

	public List<EnumOption<LayoutMode>> Layouts { get; }

	public List<LanguageOption> Languages { get; }

	public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

	public string SettingsPath => SettingsService.SettingsPath;

	[ObservableProperty]
	public partial int Page { get; set; }

	[ObservableProperty]
	public partial EnumOption<AppTheme> SelectedTheme { get; set; }

	[ObservableProperty]
	public partial EnumOption<StartupBehavior> SelectedStartup { get; set; }

	[ObservableProperty]
	public partial EnumOption<LayoutMode> SelectedLayout { get; set; }

	[ObservableProperty]
	public partial LanguageOption SelectedLanguage { get; set; }

	[ObservableProperty]
	public partial bool ShowHiddenItems { get; set; }

	[ObservableProperty]
	public partial bool ShowFileExtensions { get; set; }

	[ObservableProperty]
	public partial bool ShowThumbnails { get; set; }

	[ObservableProperty]
	public partial bool ShowDeleteConfirmation { get; set; }

	[ObservableProperty]
	public partial bool FoldersFirst { get; set; }

	[ObservableProperty]
	public partial bool EnableAnimations { get; set; }

	// ---------------- Icons ----------------

	/// <summary>Files' own icons, the desktop's icon theme, every installed icon theme, and the user's icon packs.</summary>
	[ObservableProperty]
	public partial List<EnumOption<string>> IconStyles { get; set; } = [];

	[ObservableProperty]
	public partial EnumOption<string>? SelectedIconStyle { get; set; }

	[ObservableProperty]
	public partial string? IconMessage { get; set; }

	public bool IsPackSelected => SelectedIconStyle?.Value.StartsWith("pack:", StringComparison.Ordinal) == true;

	public string IconPacksFolder => Services.Icons.IconPack.PacksDirectory;

	private bool _loadingIcons;

	private void LoadIconStyles(string? select = null)
	{
		_loadingIcons = true;
		var system = Services.Icons.IconTheme.DetectSystemTheme();
		var list = new List<EnumOption<string>>
		{
			new(Services.Icons.IconService.Builtin, Loc.Get("IconsBuiltin", "Files (Fluent)")),
			new(Services.Icons.IconService.System, system is null ? Loc.Get("IconsSystem", "System icon theme") : $"{Loc.Get("IconsSystem", "System icon theme")} ({system})"),
		};
		list.AddRange(Services.Icons.IconTheme.Installed().Select(t => new EnumOption<string>("theme:" + t.Name, $"{Loc.Get("IconTheme", "Theme")}: {t.DisplayName}")));
		list.AddRange(Services.Icons.IconPack.Installed().Select(p => new EnumOption<string>("pack:" + p.Id, $"{Loc.Get("IconPack", "Icon pack")}: {p.DisplayName}")));
		IconStyles = list;
		var wanted = select ?? _s.IconStyle;
		SelectedIconStyle = list.FirstOrDefault(o => o.Value == wanted) ?? list[0];
		_loadingIcons = false;
	}

	partial void OnSelectedIconStyleChanged(EnumOption<string>? value)
	{
		OnPropertyChanged(nameof(IsPackSelected));
		if (value is null || _loadingIcons || value.Value == _s.IconStyle)
			return;
		_s.IconStyle = value.Value;
		SettingsService.Save();
		Services.Icons.IconService.Apply(value.Value);
		IconMessage = null;
	}

	/// <summary>Imports a .zip icon pack (or a pack's icons.json) and switches to it.</summary>
	public void ImportIconPack(string path)
	{
		try
		{
			var pack = Services.Icons.IconPack.Import(path);
			LoadIconStyles();
			SelectedIconStyle = IconStyles.First(o => o.Value == "pack:" + pack.Id);
			IconMessage = $"\"{pack.DisplayName}\" was imported.";
		}
		catch (Exception ex)
		{
			IconMessage = ex.Message;
		}
	}

	/// <summary>Creates an example icon pack to start from, switches to it and opens its folder.</summary>
	[RelayCommand]
	private void CreateExampleIconPack()
	{
		var folder = Services.Icons.IconPack.CreateExample();
		LoadIconStyles();
		SelectedIconStyle = IconStyles.First(o => o.Value == "pack:" + Path.GetFileName(folder));
		Services.Icons.IconService.Apply(_s.IconStyle);
		IconMessage = $"Example pack created in {folder}. Edit icons.json and the images, then pick it again here.";
		try
		{
			LauncherService.OpenWithDefault(folder);
		}
		catch
		{
			// No file manager to show it; the path is in the message.
		}
	}

	[RelayCommand]
	private void OpenIconPacksFolder()
	{
		Directory.CreateDirectory(IconPacksFolder);
		try
		{
			LauncherService.OpenWithDefault(IconPacksFolder);
		}
		catch (Exception ex)
		{
			IconMessage = ex.Message;
		}
	}

	/// <summary>Reloads the current pack after editing its files.</summary>
	[RelayCommand]
	private void ReloadIcons()
	{
		LoadIconStyles();
		Services.Icons.IconService.Apply(_s.IconStyle);
		IconMessage = Loc.Get("IconsReloaded", "Icons reloaded.");
	}

	/// <summary>
	/// Installs the selected pack as a freedesktop icon theme (~/.local/share/icons) and, where the desktop allows it,
	/// makes it the icon theme for all apps.
	/// </summary>
	[RelayCommand]
	private void UseIconPackForAllApps()
	{
		if (Services.Icons.IconService.ActivePack is not { } pack)
			return;
		try
		{
			var system = Services.Icons.IconTheme.DetectSystemTheme();
			var inherit = system is not null && !system.StartsWith("Files-", StringComparison.Ordinal) ? system : null;
			var theme = pack.InstallAsSystemTheme(inherit);
			IconMessage = ApplySystemIconTheme(theme)
				? $"Installed and applied the \"{theme}\" icon theme for all apps."
				: $"Installed the \"{theme}\" icon theme. Choose it in your desktop's appearance settings to use it in all apps.";
		}
		catch (Exception ex)
		{
			IconMessage = ex.Message;
		}
	}

	private static bool ApplySystemIconTheme(string theme)
	{
		var desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty).ToUpperInvariant();
		if (desktop.Contains("KDE", StringComparison.Ordinal))
		{
			foreach (var tool in new[] { "/usr/lib/x86_64-linux-gnu/libexec/plasma-changeicons", "/usr/lib/aarch64-linux-gnu/libexec/plasma-changeicons", "/usr/libexec/plasma-changeicons" })
			{
				if (File.Exists(tool) && LauncherService.RunAndRead(tool, theme) is not null)
					return true;
			}
			return false;
		}
		if (desktop.Contains("XFCE", StringComparison.Ordinal))
			return LauncherService.RunAndRead("xfconf-query", "-c", "xsettings", "-p", "/Net/IconThemeName", "-s", theme) is not null;
		return LauncherService.RunAndRead("gsettings", "set", "org.gnome.desktop.interface", "icon-theme", theme) is not null;
	}

	[ObservableProperty]
	public partial bool UseSystemTitleBar { get; set; }

	[ObservableProperty]
	public partial string TerminalCommand { get; set; }

	[ObservableProperty]
	public partial bool LanguageRestartRequired { get; set; }

	partial void OnSelectedThemeChanged(EnumOption<AppTheme> value)
	{
		_s.Theme = value.Value;
		App.ApplyTheme(value.Value);
		SettingsService.Save();
	}

	partial void OnSelectedStartupChanged(EnumOption<StartupBehavior> value)
	{
		_s.Startup = value.Value;
		SettingsService.Save();
	}

	partial void OnSelectedLayoutChanged(EnumOption<LayoutMode> value)
	{
		_s.DefaultLayout = value.Value;
		SettingsService.Save();
	}

	partial void OnSelectedLanguageChanged(LanguageOption value)
	{
		if (_s.Language == value.Code)
			return;
		_s.Language = value.Code;
		LanguageRestartRequired = true;
		SettingsService.Save();
	}

	partial void OnShowHiddenItemsChanged(bool value) => Update(() => _s.ShowHiddenItems = value);

	partial void OnShowFileExtensionsChanged(bool value) => Update(() => _s.ShowFileExtensions = value);

	partial void OnShowThumbnailsChanged(bool value) => Update(() => _s.ShowThumbnails = value);

	partial void OnShowDeleteConfirmationChanged(bool value) => Update(() => _s.ShowDeleteConfirmation = value, notify: false);

	partial void OnEnableAnimationsChanged(bool value)
	{
		_s.EnableAnimations = value;
		App.ApplyMotion(value);
		SettingsService.Save();
	}

	partial void OnUseSystemTitleBarChanged(bool value) => Update(() => _s.UseSystemTitleBar = value);

	partial void OnFoldersFirstChanged(bool value) => Update(() => _s.FoldersFirst = value);

	partial void OnTerminalCommandChanged(string value) => Update(() => _s.TerminalCommand = string.IsNullOrWhiteSpace(value) ? null : value.Trim(), notify: false);

	private static void Update(Action apply, bool notify = true)
	{
		apply();
		if (notify)
			SettingsService.NotifyChanged();
		else
			SettingsService.Save();
	}

	/// <summary>Every folder goes back to the default layout and sort.</summary>
	[RelayCommand]
	private void ResetFolderViews()
	{
		FolderViewService.ResetAll();
		SettingsService.NotifyChanged();
	}

	[RelayCommand]
	private void ResetPinned()
	{
		_s.PinnedFolders = null;
		SettingsService.NotifyChanged();
	}

	[RelayCommand]
	private void OpenSettingsFile()
	{
		SettingsService.Save();
		try
		{
			LauncherService.OpenWithDefault(SettingsService.SettingsPath);
		}
		catch
		{
			// No default editor.
		}
	}
}
