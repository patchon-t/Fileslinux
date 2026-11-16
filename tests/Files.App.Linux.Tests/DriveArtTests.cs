// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Files.App.Linux.Controls;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using Files.App.Linux.Views;
using FluentIcons.Common;

namespace Files.App.Linux.Tests;

[TestClass]
public sealed class DriveArtTests
{
	[TestMethod]
	public Task DriveCards_LookLikeWindows() => TestEnvironment.GetSession().Dispatch(async () =>
	{
		SettingsService.Current.EnableAnimations = false;
		App.ApplyMotion(false);
		var vm = new MainWindowViewModel(watchMounts: false);
		var window = new MainWindow { DataContext = vm, Width = 1100, Height = 760 };
		vm.Initialize([]);
		window.Show();
		try
		{
			var tab = vm.SelectedTab!;
			for (int i = 0; i < 20 && tab.IsLoading; i++)
			{
				Dispatcher.UIThread.RunJobs();
				await Task.Delay(10);
			}

			// Sample drives: the system drive almost full (red), a data drive, a USB stick.
			tab.Home.Drives.Clear();
			tab.Home.Drives.Add(new DriveCard("Debian (/)", "/", Symbol.HardDrive, "2.77 GB free of 401 GB", 99.3, true, "ext4", IsRoot: true));
			tab.Home.Drives.Add(new DriveCard("DATA", "/media/data", Symbol.Storage, "65.4 GB free of 381 GB", 82.8, false, "ntfs3"));
			tab.Home.Drives.Add(new DriveCard("USB DRIVE", "/media/usb", Symbol.UsbStick, "12.1 GB free of 14.9 GB", 18.8, false, "vfat", IsRemovable: true));
			for (int i = 0; i < 5; i++)
			{
				Dispatcher.UIThread.RunJobs();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}

			var bars = window.GetVisualDescendants().OfType<ProgressBar>().Where(b => b.Classes.Contains("drive")).ToList();
			Assert.HasCount(3, bars);
			Assert.IsTrue(bars[0].Classes.Contains("low"), "An almost full drive shows a red bar.");
			Assert.AreEqual(16, bars[0].Bounds.Height, 0.5, "Thick bars, like Windows.");
			Assert.IsTrue(window.GetVisualDescendants().OfType<Viewbox>().Count() >= 3, "Drive pictures are drawn.");

			Save(window, "drive-cards-light");
			App.ApplyTheme(AppTheme.Dark);
			Save(window, "drive-cards-dark");
		}
		finally
		{
			App.ApplyTheme(AppTheme.Light);
			SettingsService.Current.EnableAnimations = true;
			App.ApplyMotion(true);
			window.Close();
		}
		return true;
	}, CancellationToken.None);

	[TestMethod]
	public Task SystemDrive_HasABadge() => TestEnvironment.GetSession().Dispatch(() =>
	{
		var system = (Viewbox)DriveArt.Build(48, isRemovable: false, isSystem: true);
		var plain = (Viewbox)DriveArt.Build(48, isRemovable: false, isSystem: false);
		Assert.AreEqual(((Canvas)plain.Child!).Children.Count + 1, ((Canvas)system.Child!).Children.Count, "The system drive gets the distribution logo (or Tux).");
		return true;
	}, CancellationToken.None);

	private static void Save(Window window, string name)
	{
		Dispatcher.UIThread.RunJobs();
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		if (Environment.GetEnvironmentVariable("FILES_SCREENSHOT_DIR") is { } dir && window.CaptureRenderedFrame() is { } frame)
		{
			Directory.CreateDirectory(dir);
			frame.Save(Path.Combine(dir, name + ".png"));
		}
	}
}
