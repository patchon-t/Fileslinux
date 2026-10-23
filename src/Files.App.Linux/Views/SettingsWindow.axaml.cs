// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Files.App.Linux.ViewModels;

namespace Files.App.Linux.Views;

public partial class SettingsWindow : Window
{
	public SettingsWindow()
	{
		InitializeComponent();
		DataContext = new SettingsViewModel();
		KeyDown += (_, e) =>
		{
			if (e.Key == Key.Escape)
				Close();
		};
	}
}
