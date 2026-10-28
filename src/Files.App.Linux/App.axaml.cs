// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using Files.App.Linux.Views;

namespace Files.App.Linux;

public partial class App : Application
{
	public override void Initialize() => AvaloniaXamlLoader.Load(this);

	public override void OnFrameworkInitializationCompleted()
	{
		SettingsService.Load();
		Loc.Load(SettingsService.Current.Language);
		ApplyTheme(SettingsService.Current.Theme);
		ApplyMotion(SettingsService.Current.EnableAnimations);

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			var paths = (desktop.Args ?? []).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
			var vm = new MainWindowViewModel();
			var window = new MainWindow { DataContext = vm };
			vm.Initialize(paths);
			desktop.MainWindow = window;
		}

		base.OnFrameworkInitializationCompleted();
	}

	private StyleInclude? _motionStyles;

	/// <summary>Adds or removes the Windows-style animations (Styles/Motion.axaml).</summary>
	public static void ApplyMotion(bool enabled)
	{
		if (Current is not App app)
			return;
		app._motionStyles ??= new StyleInclude(new Uri("avares://files/")) { Source = new Uri("avares://files/Styles/Motion.axaml") };
		var styles = app.Styles;
		if (enabled && !styles.Contains(app._motionStyles))
			styles.Add(app._motionStyles);
		else if (!enabled)
			styles.Remove(app._motionStyles);
	}

	public static void ApplyTheme(AppTheme theme)
	{
		if (Current is null)
			return;
		Current.RequestedThemeVariant = theme switch
		{
			AppTheme.Light => ThemeVariant.Light,
			AppTheme.Dark => ThemeVariant.Dark,
			_ => ThemeVariant.Default,
		};
	}
}
