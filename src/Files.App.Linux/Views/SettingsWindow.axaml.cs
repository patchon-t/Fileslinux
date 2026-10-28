// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
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
		KeyDown += (_, e) =>
		{
			if (e.Key == Key.Escape)
				Close();
		};
	}
}
