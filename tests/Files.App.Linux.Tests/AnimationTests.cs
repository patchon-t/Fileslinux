// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Files.App.Linux.Helpers;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using Files.App.Linux.Views;

namespace Files.App.Linux.Tests;

/// <summary>
/// Checks that the Windows-style animations run and always finish in a clean state
/// (fully opaque, no leftover offset), and that turning them off removes them.
/// Set FILES_SCREENSHOT_DIR to also save animation frames.
/// </summary>
[TestClass]
public sealed class AnimationTests
{
	private static Task RunAsync(Func<Task> action) => TestEnvironment.GetSession().Dispatch(async () =>
	{
		await action();
		return true;
	}, CancellationToken.None);

	/// <summary>Advances real time and the headless render clock so animations progress.</summary>
	private static async Task Pump(int milliseconds)
	{
		var end = Environment.TickCount64 + milliseconds;
		while (Environment.TickCount64 < end)
		{
			AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			Dispatcher.UIThread.RunJobs();
			await Task.Delay(8);
		}
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		Dispatcher.UIThread.RunJobs();
	}

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
	{
		var start = Environment.TickCount64;
		while (!condition())
		{
			if (Environment.TickCount64 - start > timeoutMs)
				Assert.Fail("Timed out waiting for condition.");
			await Pump(20);
		}
	}

	/// <summary>Runs dispatcher work without advancing the animation clock.</summary>
	private static async Task WaitFrozen(Func<bool> condition, int timeoutMs = 5000)
	{
		var start = Environment.TickCount64;
		while (!condition())
		{
			if (Environment.TickCount64 - start > timeoutMs)
				Assert.Fail("Timed out waiting for condition.");
			Dispatcher.UIThread.RunJobs();
			await Task.Delay(5);
		}
		Dispatcher.UIThread.RunJobs();
	}

	/// <summary>Renders exactly one frame (about 16 ms of animation time).</summary>
	private static void Frame(int count = 1)
	{
		for (int i = 0; i < count; i++)
		{
			AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			Dispatcher.UIThread.RunJobs();
		}
	}

	private static bool IsAtRest(Visual v) => Math.Abs(v.Opacity - 1) < 0.001 && (v.RenderTransform is null || v.RenderTransform.Value.IsIdentity);

	[TestMethod]
	public void MotionTokens_MatchWinUI()
	{
		Assert.AreEqual(83, Motion.Faster.TotalMilliseconds);
		Assert.AreEqual(167, Motion.Fast.TotalMilliseconds);
		Assert.AreEqual(250, Motion.Normal.TotalMilliseconds);
		Assert.AreEqual(350, Motion.Pane.TotalMilliseconds);
		// Decelerate (0.1,0.9 0.2,1.0) is well past halfway at 30% of the time.
		Assert.IsGreaterThan(0.75, Motion.Decelerate.Ease(0.3));
		Assert.AreEqual(1.0, Motion.Decelerate.Ease(1.0), 0.0001);
	}

	[TestMethod]
	public Task Animations_PlayAndSettle() => RunAsync(async () =>
	{
		SettingsService.Current.EnableAnimations = true;
		App.ApplyMotion(true);

		var root = TestEnvironment.NewTempDir();
		for (int i = 0; i < 30; i++)
			File.WriteAllText(Path.Combine(root, $"file {i:00}.txt"), "x");
		Directory.CreateDirectory(Path.Combine(root, "Sub"));

		var vm = new MainWindowViewModel(watchMounts: false);
		var window = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
		vm.Initialize([root]);
		window.Show();
		try
		{
			await CheckAnimationsAsync(vm, window, root);
		}
		finally
		{
			SettingsService.Current.EnableAnimations = true;
			App.ApplyMotion(true);
			window.Close();
			Dispatcher.UIThread.RunJobs();
		}
	});

	private static async Task CheckAnimationsAsync(MainWindowViewModel vm, MainWindow window, string root)
	{
		var tab = vm.SelectedTab!;
		await WaitUntil(() => !tab.IsLoading && tab.Items.Count > 0);
		await Pump(100);

		// Folder entrance: items start transparent and offset, then settle.
		tab.NavigateTo(Path.Combine(root, "Sub"));
		await WaitUntil(() => tab.CurrentPath!.EndsWith("Sub", StringComparison.Ordinal) && !tab.IsLoading);
		await Pump(600);
		tab.GoBackCommand.Execute(null);
		await WaitFrozen(() => tab.CurrentPath == root && !tab.IsLoading);
		Frame(4);
		var list = window.Shell.ActiveList;
		var containers = list.GetRealizedContainers().ToList();
		Assert.IsNotEmpty(containers);
		Assert.IsTrue(containers.Any(c => c.Opacity < 0.999), "Items should be mid-entrance right after navigating.");
		SaveFrame(window, "anim-entrance-mid");
		await Pump(900);
		Assert.IsTrue(list.GetRealizedContainers().All(IsAtRest), "Every item must end fully visible and in place.");

		// Dialog: scales/fades in, then out.
		var dialogTask = vm.Dialogs.ConfirmAsync("Delete item", "Move it to the trash?", "Delete");
		Dispatcher.UIThread.RunJobs();
		Frame(2);
		var card = window.DialogCard;
		Assert.IsTrue(window.DialogLayer.IsVisible);
		Assert.IsLessThan(0.999, card.Opacity, "The dialog should still be fading in.");
		SaveFrame(window, "anim-dialog-mid");
		await Pump(500);
		Assert.IsTrue(IsAtRest(card), "The dialog must end fully visible at normal scale.");
		window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
		await Pump(400);
		var (accepted, _) = await dialogTask;
		Assert.IsFalse(accepted);
		Assert.IsFalse(window.DialogLayer.IsVisible);

		// Sidebar: slides closed and open over 350 ms.
		var column = window.ContentGrid.ColumnDefinitions[0];
		var openWidth = column.ActualWidth;
		vm.IsSidebarOpen = false;
		Dispatcher.UIThread.RunJobs();
		Frame(4);
		Assert.IsTrue(column.Width.Value > 0 && column.Width.Value < openWidth, $"Sidebar should be mid-slide, width {column.Width.Value}.");
		SaveFrame(window, "anim-sidebar-mid");
		await Pump(500);
		Assert.AreEqual(0, column.Width.Value);
		Assert.IsFalse(window.SidebarList.IsVisible);
		vm.IsSidebarOpen = true;
		await Pump(600);
		Assert.AreEqual(openWidth, column.Width.Value, 1);
		Assert.IsTrue(window.SidebarList.IsVisible);
		Assert.IsTrue(IsAtRest(window.SidebarList));

		// Turning animations off removes the motion styles and makes changes instant.
		SettingsService.Current.EnableAnimations = false;
		App.ApplyMotion(false);
		vm.IsSidebarOpen = false;
		Assert.AreEqual(0, column.Width.Value);
		vm.IsSidebarOpen = true;
		Assert.AreEqual(openWidth, column.Width.Value, 1);

	}

	/// <summary>
	/// Records frame sequences of each animation for docs/linux-animations.md
	/// (only when FILES_ANIMATION_FRAMES_DIR is set; frames are turned into GIFs by the docs script).
	/// </summary>
	[TestMethod]
	public Task RecordAnimationFrames() => RunAsync(async () =>
	{
		if (Environment.GetEnvironmentVariable("FILES_ANIMATION_FRAMES_DIR") is not { } dir)
			return;

		SettingsService.Current.EnableAnimations = true;
		App.ApplyMotion(true);
		var root = Path.Combine(XdgPaths.Home, "Documents");
		Directory.CreateDirectory(root);
		foreach (var d in new[] { "Invoices", "Projects", "Recipes" })
			Directory.CreateDirectory(Path.Combine(root, d));
		foreach (var f in new[] { "Budget 2026.xlsx", "Cover letter.docx", "Meeting notes.md", "Pitch deck.pptx", "Quarterly report.pdf", "todo.txt", "backup.sh", "photos.zip", "song.mp3" })
			File.WriteAllText(Path.Combine(root, f), "sample text");

		var vm = new MainWindowViewModel(watchMounts: false);
		var window = new MainWindow { DataContext = vm, Width = 960, Height = 600 };
		vm.Initialize([Path.Combine(root, "Projects")]);
		window.Show();
		var tab = vm.SelectedTab!;
		try
		{
			await WaitUntil(() => !tab.IsLoading);
			await Pump(800);

			// Frames are kept in memory and saved afterwards so capturing stays fast; the real time between
			// frames is written to delays.txt (in 1/100 s) so the GIFs play back at true speed.
			async Task Record(string name, int frames, Action start)
			{
				var folder = Path.Combine(dir, name);
				Directory.CreateDirectory(folder);
				var captured = new List<(Avalonia.Media.Imaging.Bitmap Frame, double Ms)>();
				var watch = System.Diagnostics.Stopwatch.StartNew();
				var last = 0.0;
				start();
				for (int i = 0; i < frames; i++)
				{
					while (watch.Elapsed.TotalMilliseconds - last < 1000 / 60.0)
						await Task.Delay(1);
					Dispatcher.UIThread.RunJobs();
					AvaloniaHeadlessPlatform.ForceRenderTimerTick();
					Dispatcher.UIThread.RunJobs();
					var now = watch.Elapsed.TotalMilliseconds;
					if (window.CaptureRenderedFrame() is { } frame)
						captured.Add((frame, now - last));
					last = now;
				}
				var delays = new List<string>();
				for (int i = 0; i < captured.Count; i++)
				{
					captured[i].Frame.Save(Path.Combine(folder, $"{i:000}.png"));
					var next = i + 1 < captured.Count ? captured[i + 1].Ms : 1000;
					delays.Add(Math.Max(2, (int)Math.Round(next / 10)).ToString(System.Globalization.CultureInfo.InvariantCulture));
				}
				File.WriteAllLines(Path.Combine(folder, "delays.txt"), delays);
				await Pump(500);
			}

			await Record("folder-entrance", 28, () => tab.NavigateTo(root));
			await Record("details-pane", 22, () =>
			{
				tab.SetSelection([tab.Items.First(i => i.Name == "Meeting notes.md")]);
				vm.IsInfoPaneOpen = true;
			});
			vm.IsInfoPaneOpen = false;
			await Pump(300);
			Task<(bool, bool)>? dialog = null;
			await Record("dialog", 22, () => dialog = vm.Dialogs.ConfirmAsync("Delete item", "Are you sure you want to move \"todo.txt\" to the trash?", "Delete", "Permanently delete", isDestructive: true));
			window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
			await Pump(500);
			await dialog!;
			await Record("sidebar", 28, () => vm.IsSidebarOpen = false);
			await Record("sidebar-open", 28, () => vm.IsSidebarOpen = true);
			await Record("home", 30, () => tab.NavigateTo(null));
		}
		finally
		{
			vm.IsInfoPaneOpen = false;
			window.Close();
			Dispatcher.UIThread.RunJobs();
		}
	});

	private static void SaveFrame(Window window, string name)
	{
		if (Environment.GetEnvironmentVariable("FILES_SCREENSHOT_DIR") is not { } dir)
			return;
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		if (window.CaptureRenderedFrame() is { } frame)
		{
			Directory.CreateDirectory(dir);
			frame.Save(Path.Combine(dir, name + ".png"));
		}
	}
}
