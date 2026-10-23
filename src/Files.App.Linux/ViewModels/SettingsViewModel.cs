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
