// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Files.App.Linux.ViewModels;

namespace Files.App.Linux.Views;

public partial class PropertiesWindow : Window
{
	public PropertiesWindow()
		: this([])
	{
	}

	public PropertiesWindow(IReadOnlyList<string> paths)
	{
		InitializeComponent();
		if (paths.Count > 0)
			DataContext = new PropertiesViewModel(paths);
		Closed += (_, _) => (DataContext as PropertiesViewModel)?.Cancel();
		KeyDown += (_, e) =>
		{
			if (e.Key == Key.Escape)
				Close();
		};
	}
}
