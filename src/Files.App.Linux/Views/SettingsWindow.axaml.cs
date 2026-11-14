// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Files.App.Linux.Helpers;
using Files.App.Linux.ViewModels;

namespace Files.App.Linux.Views;

public partial class SettingsWindow : Window
{
	public SettingsWindow()
	{
		InitializeComponent();
		var vm = new SettingsViewModel();
		DataContext = vm;

		// Each settings page rises in when selected, like the Windows Settings dialog.
		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(SettingsViewModel.Page) && Pages.Children.FirstOrDefault(c => c.IsVisible) is { } page)
				_ = Motion.EntranceAsync(page, offsetY: 16);
		};
		ImportIconPackButton.Click += async (_, _) =>
		{
			var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = Services.Loc.Get("ImportIconPack", "Import icon pack"),
				AllowMultiple = false,
				FileTypeFilter =
				[
					new FilePickerFileType("Icon pack") { Patterns = ["*.zip", "icons.json"] },
				],
			});
			if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
				vm.ImportIconPack(path);
		};
		KeyDown += (_, e) =>
		{
			if (e.Key == Key.Escape)
				Close();
		};
	}
}
