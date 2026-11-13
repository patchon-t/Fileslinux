// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using Files.App.Linux.Views;

namespace Files.App.Linux.Tests;

[TestClass]
public sealed class TitleBarAndMenuTests
{
	private static Task RunAsync(Func<Task> action) => TestEnvironment.GetSession().Dispatch(async () =>
	{
		await action();
		return true;
	}, CancellationToken.None);

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
	{
		var start = Environment.TickCount64;
		while (!condition())
		{
			if (Environment.TickCount64 - start > timeoutMs)
				Assert.Fail("Timed out waiting for condition.");
			Dispatcher.UIThread.RunJobs();
			await Task.Delay(20);
		}
	}

	private static async Task<(MainWindow Window, MainWindowViewModel Vm)> OpenAsync()
	{
		var root = TestEnvironment.NewTempDir();
		File.WriteAllText(Path.Combine(root, "a.txt"), "a");
		var vm = new MainWindowViewModel(watchMounts: false);
		var window = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
		vm.Initialize([root]);
		window.Show();
		await WaitUntil(() => vm.SelectedTab is { IsLoading: false });
		return (window, vm);
	}

	private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

	[TestMethod]
	public Task ToolbarDropdowns_OpenWithItems() => RunAsync(async () =>
	{
		var (window, _) = await OpenAsync();
		try
		{
			var shell = window.Shell;
			foreach (var name in new[] { "SortButton", "LayoutButton", "MoreButton" })
			{
				var button = shell.FindControl<Button>(name)!;
				Click(button);
				Dispatcher.UIThread.RunJobs();
				var menu = shell.LastToolbarMenu;
				Assert.IsNotNull(menu, $"{name} should open a menu.");
				Assert.IsTrue(menu.IsOpen, $"{name} menu should be open.");
				Assert.IsGreaterThan(2, menu.Items.Count, $"{name} menu should have items.");
				menu.Hide();
			}
		}
		finally
		{
			window.Close();
		}
	});

	[TestMethod]
	public Task CustomTitleBar_CaptionButtonsAndSystemOption() => RunAsync(async () =>
	{
		SettingsService.Current.UseSystemTitleBar = false;
		var (window, _) = await OpenAsync();
		try
		{
			// Tabs share the title bar: no desktop decorations, our caption buttons are shown.
			Assert.AreEqual(SystemDecorations.None, window.SystemDecorations);
			Assert.IsTrue(window.CaptionButtons.IsVisible);
			Assert.IsTrue(window.ResizeGrips.IsVisible);

			Click(window.MaximizeButton);
			Assert.AreEqual(WindowState.Maximized, window.WindowState);
			Assert.IsFalse(window.ResizeGrips.IsVisible, "No resize edges while maximized.");
			Click(window.MaximizeButton);
			Assert.AreEqual(WindowState.Normal, window.WindowState);

			// Refresh spins the icon without breaking anything.
			Click(window.RefreshButton);
			Dispatcher.UIThread.RunJobs();

			// The desktop's own title bar can be used instead.
			SettingsService.Current.UseSystemTitleBar = true;
			SettingsService.NotifyChanged();
			Assert.AreEqual(SystemDecorations.Full, window.SystemDecorations);
			Assert.IsFalse(window.CaptionButtons.IsVisible);
			Assert.IsFalse(window.ResizeGrips.IsVisible);
		}
		finally
		{
			SettingsService.Current.UseSystemTitleBar = false;
			SettingsService.Save();
			window.Close();
		}
	});
}

[TestClass]
public sealed class ShortcutLabelTests
{
	[TestMethod]
	public void DigitShortcuts_ShowTheDigit()
	{
		var text = Files.App.Linux.Helpers.MenuBuilder.Gesture("Ctrl+Shift+1").ToString("p", null);
		Assert.DoesNotContain("Cancel", text);
		StringAssert.EndsWith(text, "1");
	}
}
