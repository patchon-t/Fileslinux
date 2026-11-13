// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Files.App.Linux.Models;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using Files.App.Linux.Views;

namespace Files.App.Linux.Tests;

/// <summary>Per-folder views, undo/redo, shortcuts, drag-select, tab tear-out and Ctrl + wheel.</summary>
[TestClass]
public sealed class FeatureTests
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
			AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			await Task.Delay(15);
		}
	}

	private static string MakeFolder(params string[] files)
	{
		var root = TestEnvironment.NewTempDir();
		foreach (var f in files)
			File.WriteAllText(Path.Combine(root, f), f);
		return root;
	}

	/// <summary>Turns animations off so items sit at their final positions when the test measures them.</summary>
	private static void NoMotion()
	{
		SettingsService.Current.EnableAnimations = false;
		App.ApplyMotion(false);
	}

	private static void RestoreMotion()
	{
		SettingsService.Current.EnableAnimations = true;
		App.ApplyMotion(true);
	}

	private static async Task Settle()
	{
		for (int i = 0; i < 5; i++)
		{
			Dispatcher.UIThread.RunJobs();
			AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			await Task.Delay(10);
		}
	}

	private static async Task<(MainWindow Window, MainWindowViewModel Vm, ShellTabViewModel Tab)> OpenAsync(string path)
	{
		var vm = new MainWindowViewModel(watchMounts: false) { Dialogs = new NullDialogService() };
		var window = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
		vm.Dialogs = new NullDialogService();
		vm.Initialize([path]);
		window.Show();
		vm.Dialogs = new NullDialogService();
		var tab = vm.SelectedTab!;
		await WaitUntil(() => !tab.IsLoading && tab.CurrentPath == path);
		return (window, vm, tab);
	}

	[TestMethod]
	public Task FolderView_IsRememberedPerFolder() => RunAsync(async () =>
	{
		FolderViewService.ResetAll();
		SettingsService.Current.DefaultLayout = LayoutMode.Details;
		var a = MakeFolder("x.txt");
		var b = MakeFolder("y.txt");
		var (window, vm, tab) = await OpenAsync(a);
		try
		{
			Assert.AreEqual(LayoutMode.Details, tab.Layout, "A new folder opens with the default layout.");

			tab.SetLayoutCommand.Execute(LayoutMode.GridSmall);
			tab.SetSortCommand.Execute(SortColumn.Size);
			Assert.AreEqual(LayoutMode.GridSmall, tab.Layout);
			Assert.AreEqual(SortColumn.Size, tab.SortBy);

			tab.NavigateTo(b);
			await WaitUntil(() => tab.CurrentPath == b && !tab.IsLoading);
			Assert.AreEqual(LayoutMode.Details, tab.Layout, "Other folders keep the default layout.");
			Assert.AreEqual(SortColumn.Name, tab.SortBy, "Other folders keep the default sort.");
			Assert.AreEqual(LayoutMode.Details, SettingsService.Current.DefaultLayout, "The default is not changed.");

			tab.GoBackCommand.Execute(null);
			await WaitUntil(() => tab.CurrentPath == a && !tab.IsLoading);
			Assert.AreEqual(LayoutMode.GridSmall, tab.Layout, "Folder A remembers its layout.");
			Assert.AreEqual(SortColumn.Size, tab.SortBy);
			Assert.IsTrue(tab.HasCustomView);

			// Another tab on the same folder follows along; one on another folder doesn't.
			var other = vm.OpenTab(a, activate: false);
			var third = vm.OpenTab(b, activate: false);
			await WaitUntil(() => !other.IsLoading && !third.IsLoading);
			tab.SetLayoutCommand.Execute(LayoutMode.GridLarge);
			Assert.AreEqual(LayoutMode.GridLarge, other.Layout);
			Assert.AreEqual(LayoutMode.Details, third.Layout);

			// Saved in settings, and can go back to the default.
			Assert.IsTrue(SettingsService.Current.FolderViews.ContainsKey(a));
			tab.ResetFolderViewCommand.Execute(null);
			Assert.AreEqual(LayoutMode.Details, tab.Layout);
			Assert.IsFalse(tab.HasCustomView);
		}
		finally
		{
			FolderViewService.ResetAll();
			window.Close();
		}
	});

	[TestMethod]
	public Task Undo_RedoRenameTrashAndCopy() => RunAsync(async () =>
	{
		UndoService.Clear();
		var root = MakeFolder("report.txt", "notes.txt");
		var (window, vm, tab) = await OpenAsync(root);
		try
		{
			// Rename (the null dialog service accepts the suggested name, so rename via the service + record).
			var renamed = FileOperationsService.Rename(Path.Combine(root, "report.txt"), "final.txt");
			UndoService.Record(UndoService.ForRename(Path.Combine(root, "report.txt"), renamed));
			await vm.UndoCommand.ExecuteAsync(null);
			Assert.IsTrue(File.Exists(Path.Combine(root, "report.txt")));
			await vm.RedoCommand.ExecuteAsync(null);
			Assert.IsTrue(File.Exists(Path.Combine(root, "final.txt")));

			// Move to trash through the app, then undo restores it.
			await WaitUntil(() => tab.Items.Any(i => i.Name == "notes.txt"));
			tab.SetSelection([tab.Items.First(i => i.Name == "notes.txt")]);
			await tab.DeleteCommand.ExecuteAsync(null);
			Assert.IsFalse(File.Exists(Path.Combine(root, "notes.txt")));
			window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
			await WaitUntil(() => File.Exists(Path.Combine(root, "notes.txt")));

			// Copy into the same folder, then undo removes the copy.
			await vm.TransferAsync([Path.Combine(root, "notes.txt")], root, move: false);
			Assert.IsTrue(File.Exists(Path.Combine(root, "notes (2).txt")));
			await vm.UndoCommand.ExecuteAsync(null);
			Assert.IsFalse(File.Exists(Path.Combine(root, "notes (2).txt")));
			window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);
			await WaitUntil(() => File.Exists(Path.Combine(root, "notes (2).txt")));
		}
		finally
		{
			UndoService.Clear();
			window.Close();
		}
	});

	[TestMethod]
	public Task Shortcuts_WorkAfterClickingAToolbarButton() => RunAsync(async () =>
	{
		var root = MakeFolder("a.txt");
		var (window, _, tab) = await OpenAsync(root);
		try
		{
			await WaitUntil(() => tab.Items.Count == 1);
			tab.SetSelection([tab.Items[0]]);
			window.Shell.FindControl<Button>("CopyButton")!.Focus();
			ClipboardService.Clear();
			window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
			await WaitUntil(() => ClipboardService.Paths.Count == 1);
			window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
			await WaitUntil(() => File.Exists(Path.Combine(root, "a (2).txt")));
			StringAssert.Contains(ToolTip.GetTip(window.Shell.FindControl<Button>("CopyButton")!)?.ToString(), "Ctrl+C");
		}
		finally
		{
			window.Close();
		}
	});

	[TestMethod]
	public Task DragSelectBox_SelectsItemsItCrosses() => RunAsync(async () =>
	{
		NoMotion();
		var root = MakeFolder("1.txt", "2.txt", "3.txt", "4.txt", "5.txt");
		var (window, _, tab) = await OpenAsync(root);
		try
		{
			tab.SetLayoutCommand.Execute(LayoutMode.Details);
			await WaitUntil(() => tab.Items.Count == 5);
			await WaitUntil(() => window.Shell.ActiveList.GetRealizedContainers().Count() == 5);
			await Settle();
			var list = window.Shell.ActiveList;
			var first = list.ContainerFromIndex(1)!;
			var start = list.TranslatePoint(new Point(400, list.Bounds.Height - 20), window)!.Value; // empty space below the rows
			var end = first.TranslatePoint(new Point(200, 10), window)!.Value;              // up into the second row

			window.MouseDown(start, MouseButton.Left);
			window.MouseMove(new Point(start.X - 5, start.Y - 30), RawInputModifiers.LeftMouseButton);
			window.MouseMove(end, RawInputModifiers.LeftMouseButton);
			Dispatcher.UIThread.RunJobs();
			Assert.IsTrue(window.Shell.IsMarqueeActive, "The selection box should be showing.");
			Assert.AreEqual(4, tab.SelectedItems.Count, "Rows 2-5 are inside the box. Got: " + string.Join(",", tab.SelectedItems.Select(i => i.Name)) + $" start={start} end={end} rows=" + string.Join(";", list.GetRealizedContainers().Select(c => $"{(c.DataContext as FileItem)?.Name}@{c.TranslatePoint(default, window)}")));
			window.MouseUp(end, MouseButton.Left);
			Dispatcher.UIThread.RunJobs();
			Assert.IsFalse(window.Shell.IsMarqueeActive);
			Assert.AreEqual(4, tab.SelectedItems.Count);
		}
		finally
		{
			RestoreMotion();
			window.Close();
		}
	});

	[TestMethod]
	public Task Tabs_ReorderAndTearOut() => RunAsync(async () =>
	{
		NoMotion();
		var a = MakeFolder("a.txt");
		var b = MakeFolder("b.txt");
		var (window, vm, tab) = await OpenAsync(a);
		MainWindow? torn = null;
		try
		{
			tab.NavigateTo(b);
			await WaitUntil(() => tab.CurrentPath == b && !tab.IsLoading);
			var second = vm.OpenTab(a);
			vm.MoveTab(second, 0);
			Assert.AreSame(second, vm.Tabs[0]);

			// Dragging the first tab well below the tab bar opens it in its own window, with its history.
			await WaitUntil(() => window.TabList.ContainerFromIndex(1)?.DataContext == tab);
			await Settle();
			var container = window.TabList.ContainerFromIndex(1)!;
			var startPoint = container.TranslatePoint(new Point(40, 15), window)!.Value;
			window.MouseDown(startPoint, MouseButton.Left);
			window.MouseMove(new Point(startPoint.X + 10, startPoint.Y + 2), RawInputModifiers.LeftMouseButton);
			window.MouseMove(new Point(startPoint.X + 20, startPoint.Y + 200), RawInputModifiers.LeftMouseButton);
			window.MouseUp(new Point(startPoint.X + 20, startPoint.Y + 200), MouseButton.Left);
			Dispatcher.UIThread.RunJobs();
			Assert.AreEqual(1, vm.Tabs.Count, "The tab left this window.");

			torn = MainWindow.OpenWindows.FirstOrDefault(w => w != window);
			Assert.IsNotNull(torn, "A new window opened for the tab.");
			var tornVm = (MainWindowViewModel)torn.DataContext!;
			Assert.AreEqual(b, tornVm.SelectedTab!.CurrentPath);
			Assert.IsTrue(tornVm.SelectedTab.CanGoBack, "History came along.");
		}
		finally
		{
			RestoreMotion();
			torn?.Close();
			window.Close();
		}
	});

	[TestMethod]
	public Task CtrlWheel_ChangesItemSize() => RunAsync(async () =>
	{
		FolderViewService.ResetAll();
		var root = MakeFolder("a.txt");
		var (window, _, tab) = await OpenAsync(root);
		try
		{
			tab.SetLayoutCommand.Execute(LayoutMode.Details);
			var list = window.Shell.ActiveList;
			var p = list.TranslatePoint(new Point(100, 100), window)!.Value;
			window.MouseWheel(p, new Vector(0, 1), RawInputModifiers.Control);
			Assert.AreEqual(LayoutMode.List, tab.Layout);
			window.MouseWheel(p, new Vector(0, 1), RawInputModifiers.Control);
			Assert.AreEqual(LayoutMode.GridSmall, tab.Layout);
			window.MouseWheel(p, new Vector(0, -1), RawInputModifiers.Control);
			Assert.AreEqual(LayoutMode.List, tab.Layout);
		}
		finally
		{
			FolderViewService.ResetAll();
			window.Close();
		}
	});
}
