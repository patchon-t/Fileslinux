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

/// <summary>Drives the real window headlessly. Set FILES_SCREENSHOT_DIR to save PNG screenshots.</summary>
[TestClass]
public sealed class UiTests
{
	private static string? ScreenshotDir => Environment.GetEnvironmentVariable("FILES_SCREENSHOT_DIR");

	private static Task RunAsync(Func<Task> action) => TestEnvironment.GetSession().Dispatch(async () =>
	{
		await action();
		return true;
	}, CancellationToken.None);

	private static string CreateSampleFolder()
	{
		var root = TestEnvironment.NewTempDir();
		foreach (var d in new[] { "Documents", "Projects", "Photos", "Music" })
			Directory.CreateDirectory(Path.Combine(root, d));
		File.WriteAllText(Path.Combine(root, "notes.txt"), "Shopping list\n- milk\n- bread\n");
		File.WriteAllText(Path.Combine(root, "readme.md"), "# Files for Linux\n\nA modern file manager.");
		File.WriteAllText(Path.Combine(root, "budget.xlsx"), new string('x', 24000));
		File.WriteAllText(Path.Combine(root, "report.pdf"), new string('x', 180000));
		File.WriteAllText(Path.Combine(root, "song.mp3"), new string('x', 4000000));
		File.WriteAllText(Path.Combine(root, "archive.zip"), "PK");
		File.WriteAllText(Path.Combine(root, "script.sh"), "#!/bin/sh\necho hi\n");
		File.WriteAllText(Path.Combine(root, ".hidden"), "secret");
		return root;
	}

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
	{
		var start = Environment.TickCount64;
		while (!condition())
		{
			if (Environment.TickCount64 - start > timeoutMs)
				Assert.Fail("Timed out waiting for condition.");
			await Task.Delay(20);
			Dispatcher.UIThread.RunJobs();
		}
	}

	private static void Render(Window window, string name)
	{
		Dispatcher.UIThread.RunJobs();
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		var frame = window.CaptureRenderedFrame();
		if (ScreenshotDir is { } dir && frame is not null)
		{
			Directory.CreateDirectory(dir);
			frame.Save(Path.Combine(dir, name + ".png"));
		}
	}

	private static (MainWindow Window, MainWindowViewModel Vm) Open(string? path, double width = 1280, double height = 800)
	{
		var vm = new MainWindowViewModel(watchMounts: false);
		var window = new MainWindow { DataContext = vm, Width = width, Height = height };
		vm.Initialize(path is null ? [] : [path]);
		window.Show();
		return (window, vm);
	}

	[TestMethod]
	public Task HomePage_ShowsQuickAccessAndDrives() => RunAsync(async () =>
	{
		var (window, vm) = Open(null);
		await WaitUntil(() => vm.SelectedTab is { IsHome: true, IsLoading: false });
		Assert.IsGreaterThan(0, vm.SelectedTab!.Home.QuickAccess.Count + vm.SelectedTab.Home.Drives.Count);
		Assert.AreEqual(SidebarItemKind.Home, vm.Sidebar.SelectedItem?.Kind);
		Render(window, "01-home-light");
		window.Close();
	});

	[TestMethod]
	public Task Folder_DetailsLayout_ListsItemsFoldersFirst() => RunAsync(async () =>
	{
		var root = CreateSampleFolder();
		var (window, vm) = Open(root);
		var tab = vm.SelectedTab!;
		await WaitUntil(() => !tab.IsLoading && tab.Items.Count > 0);

		Assert.AreEqual(11, tab.Items.Count, "Hidden files are not listed by default.");
		Assert.IsTrue(tab.Items.Take(4).All(i => i.IsDirectory), "Folders come first.");
		Assert.AreEqual("Documents", tab.Items[0].Name);
		Assert.AreEqual(Path.GetFileName(root), tab.Breadcrumbs.Last().Name);

		tab.SetSelection([tab.Items.First(i => i.Name == "notes.txt")]);
		vm.IsInfoPaneOpen = true;
		await WaitUntil(() => tab.Preview.HasText);
		Render(window, "02-details-light");
		vm.IsInfoPaneOpen = false;
		window.Close();
	});

	[TestMethod]
	public Task Navigation_BackForwardUp() => RunAsync(async () =>
	{
		var root = CreateSampleFolder();
		var (window, vm) = Open(root);
		var tab = vm.SelectedTab!;
		await WaitUntil(() => !tab.IsLoading);

		tab.OpenItem(tab.Items.First(i => i.Name == "Projects"));
		await WaitUntil(() => tab.CurrentPath == Path.Combine(root, "Projects") && !tab.IsLoading);
		Assert.IsTrue(tab.IsEmpty);
		Assert.IsTrue(tab.CanGoBack);

		tab.GoBackCommand.Execute(null);
		await WaitUntil(() => tab.CurrentPath == root);
		Assert.IsTrue(tab.CanGoForward);

		tab.GoForwardCommand.Execute(null);
		await WaitUntil(() => tab.CurrentPath == Path.Combine(root, "Projects"));

		tab.GoUpCommand.Execute(null);
		await WaitUntil(() => tab.CurrentPath == root);

		tab.NavigateTo("~");
		await WaitUntil(() => tab.CurrentPath == XdgPaths.Home);
		window.Close();
	});

	[TestMethod]
	public Task Tabs_OpenCloseAndReopen() => RunAsync(async () =>
	{
		var root = CreateSampleFolder();
		var (window, vm) = Open(root);
		await WaitUntil(() => !vm.SelectedTab!.IsLoading);

		vm.NewTabCommand.Execute(null);
		vm.OpenTab(Path.Combine(root, "Photos"));
		Assert.AreEqual(3, vm.Tabs.Count);
		Assert.AreEqual(Path.Combine(root, "Photos"), vm.SelectedTab!.CurrentPath);
		await WaitUntil(() => !vm.SelectedTab.IsLoading);
		Render(window, "03-tabs");

		vm.CloseTab(vm.SelectedTab);
		Assert.AreEqual(2, vm.Tabs.Count);
		vm.ReopenClosedTabCommand.Execute(null);
		Assert.AreEqual(Path.Combine(root, "Photos"), vm.SelectedTab!.CurrentPath);
		window.Close();
	});

	[TestMethod]
	public Task Layouts_GridAndDarkTheme() => RunAsync(async () =>
	{
		var root = CreateSampleFolder();
		var (window, vm) = Open(root);
		var tab = vm.SelectedTab!;
		await WaitUntil(() => !tab.IsLoading);

		tab.SetLayoutCommand.Execute(LayoutMode.GridMedium);
		Assert.IsTrue(tab.IsGridLayout);
		Render(window, "04-grid-light");

		App.ApplyTheme(AppTheme.Dark);
		tab.SetLayoutCommand.Execute(LayoutMode.Details);
		Render(window, "05-details-dark");

		tab.NavigateTo(null);
		await WaitUntil(() => tab.IsHome);
		Render(window, "06-home-dark");

		App.ApplyTheme(AppTheme.Light);
		tab.SetLayoutCommand.Execute(LayoutMode.Details);
		window.Close();
	});

	[TestMethod]
	public Task Keyboard_CopyPasteAndNewFolder() => RunAsync(async () =>
	{
		var root = CreateSampleFolder();
		var (window, vm) = Open(root);
		var tab = vm.SelectedTab!;
		await WaitUntil(() => !tab.IsLoading);

		// Select notes.txt and copy with Ctrl+C, then paste with Ctrl+V into the same folder.
		tab.SetSelection([tab.Items.First(i => i.Name == "notes.txt")]);
		window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
		await WaitUntil(() => ClipboardService.Paths.Count == 1);
		window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
		await WaitUntil(() => File.Exists(Path.Combine(root, "notes (2).txt")));

		// Ctrl+Shift+N shows the new-folder dialog; Enter accepts the suggested name.
		window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control | RawInputModifiers.Shift);
		await WaitUntil(() => window.DialogLayer.IsVisible);
		Render(window, "07-new-folder-dialog");
		window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
		await WaitUntil(() => Directory.Exists(Path.Combine(root, "New folder")));
		await WaitUntil(() => tab.SelectedItems.Any(i => i.Name == "New folder"));
		window.Close();
	});

	[TestMethod]
	public Task Delete_MovesToTrash_AndRestore() => RunAsync(async () =>
	{
		var root = CreateSampleFolder();
		var (window, vm) = Open(root);
		var tab = vm.SelectedTab!;
		await WaitUntil(() => !tab.IsLoading);
		var dialogs = vm.Dialogs;
		vm.Dialogs = new NullDialogService();

		tab.SetSelection([tab.Items.First(i => i.Name == "readme.md")]);
		await tab.DeleteCommand.ExecuteAsync(null);
		Assert.IsFalse(File.Exists(Path.Combine(root, "readme.md")));

		tab.NavigateTo(TrashService.TrashUri);
		await WaitUntil(() => tab.IsTrash && !tab.IsLoading && tab.Items.Count > 0);
		var trashed = tab.Items.First(i => i.Name == "readme.md");
		Assert.IsNotNull(trashed.TrashEntry);
		Render(window, "08-trash");

		tab.SetSelection([trashed]);
		await tab.RestoreCommand.ExecuteAsync(null);
		Assert.IsTrue(File.Exists(Path.Combine(root, "readme.md")));
		vm.Dialogs = dialogs;
		window.Close();
	});

	[TestMethod]
	public Task Search_FindsNestedFiles() => RunAsync(async () =>
	{
		var root = CreateSampleFolder();
		File.WriteAllText(Path.Combine(root, "Projects", "deep-report.txt"), "x");
		var (window, vm) = Open(root);
		var tab = vm.SelectedTab!;
		await WaitUntil(() => !tab.IsLoading);

		await tab.SearchAsync("report");
		Assert.IsTrue(tab.IsSearchResults);
		CollectionAssert.AreEquivalent(new[] { "report.pdf", "deep-report.txt" }, tab.Items.Select(i => i.Name).ToArray());
		Render(window, "09-search");
		window.Close();
	});

	[TestMethod]
	public Task SettingsAndProperties_Render() => RunAsync(async () =>
	{
		var root = CreateSampleFolder();
		var settings = new SettingsWindow { Width = 860, Height = 600 };
		settings.Show();
		((SettingsViewModel)settings.DataContext!).Page = 2;
		Render(settings, "10-settings");
		settings.Close();

		var props = new PropertiesWindow([root]) { Width = 460, Height = 620 };
		props.Show();
		var pvm = (PropertiesViewModel)props.DataContext!;
		await WaitUntil(() => pvm.HasContains);
		StringAssert.Contains(pvm.ContainsText, "files");
		Render(props, "11-properties");
		props.Close();
	});
	/// <summary>Builds a realistic demo home and saves the screenshots used in the README (only when FILES_SCREENSHOT_DIR is set).</summary>
	[TestMethod]
	public Task ReadmeScreenshots() => RunAsync(async () =>
	{
		if (ScreenshotDir is null)
			return;

		var home = XdgPaths.Home;
		var docs = Path.Combine(home, "Documents");
		foreach (var d in new[] { "Invoices", "Projects", "Recipes", "Taxes 2026" })
			Directory.CreateDirectory(Path.Combine(docs, d));
		File.WriteAllText(Path.Combine(docs, "Meeting notes.md"), "# Weekly sync\n\n- Ship the Linux build\n- Review the new sidebar\n- Plan the 1.1 release\n\nOwner: Alex\n");
		File.WriteAllText(Path.Combine(docs, "Quarterly report.pdf"), new string('x', 482_000));
		File.WriteAllText(Path.Combine(docs, "Budget 2026.xlsx"), new string('x', 38_500));
		File.WriteAllText(Path.Combine(docs, "Pitch deck.pptx"), new string('x', 2_400_000));
		File.WriteAllText(Path.Combine(docs, "Cover letter.docx"), new string('x', 21_300));
		File.WriteAllText(Path.Combine(docs, "Holiday photos.zip"), new string('x', 15_700_000));
		File.WriteAllText(Path.Combine(docs, "todo.txt"), "buy milk\ncall mom\n");
		File.WriteAllText(Path.Combine(docs, "backup.sh"), "#!/bin/sh\nrsync -a ~/Documents /mnt/backup\n");

		// Colorful sample images for thumbnails.
		var pics = Path.Combine(home, "Pictures");
		var palette = new (string Name, string A, string B)[]
		{
			("Sunset.png", "#FF7E5F", "#FEB47B"), ("Ocean.png", "#2193B0", "#6DD5ED"), ("Forest.png", "#11998E", "#38EF7D"),
			("Lavender.png", "#8E2DE2", "#C471F5"), ("Dawn.png", "#F7971E", "#FFD200"), ("Night sky.png", "#141E30", "#4B6CB7"),
			("Berry.png", "#C33764", "#F06292"), ("Glacier.png", "#83A4D4", "#E0F7FA"),
		};
		foreach (var (name, a, b) in palette)
		{
			var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(400, 300));
			using (var ctx = bmp.CreateDrawingContext())
			{
				var brush = new Avalonia.Media.LinearGradientBrush
				{
					StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
					EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
					GradientStops = { new(Avalonia.Media.Color.Parse(a), 0), new(Avalonia.Media.Color.Parse(b), 1) },
				};
				ctx.FillRectangle(brush, new Rect(0, 0, 400, 300));
				ctx.DrawEllipse(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(70, 255, 255, 255)), null, new Point(290, 110), 60, 60);
			}
			bmp.Save(Path.Combine(pics, name));
		}
		Directory.CreateDirectory(Path.Combine(pics, "Screenshots"));
		Directory.CreateDirectory(Path.Combine(pics, "Wallpapers"));

		// Recent files, in the GTK recently-used.xbel format the Home page reads.
		var recent = new[] { "Meeting notes.md", "Budget 2026.xlsx", "Quarterly report.pdf", "Pitch deck.pptx" };
		var xbel = "<?xml version=\"1.0\"?>\n<xbel version=\"1.0\">\n" + string.Join("\n", recent.Select((n, i) =>
			$"  <bookmark href=\"{new Uri(Path.Combine(docs, n)).AbsoluteUri}\" modified=\"2026-10-2{4 - i}T10:00:00Z\" />")) + "\n</xbel>\n";
		Directory.CreateDirectory(XdgPaths.DataHome);
		File.WriteAllText(Path.Combine(XdgPaths.DataHome, "recently-used.xbel"), xbel);

		var settings = SettingsService.Current;
		settings.PinnedFolders = null;

		// Home
		var (window, vm) = Open(null);
		var tab = vm.SelectedTab!;
		await WaitUntil(() => tab.IsHome && tab.Home.QuickAccess.Count > 0 && tab.Home.HasRecentFiles);
		vm.OpenTab(docs, activate: false);
		vm.OpenTab(pics, activate: false);
		Render(window, "readme-home");

		// Details + details pane
		tab.NavigateTo(docs);
		await WaitUntil(() => !tab.IsLoading && tab.Items.Count > 0);
		tab.SetLayoutCommand.Execute(LayoutMode.Details);
		var notes = tab.Items.First(i => i.Name == "Meeting notes.md");
		vm.IsInfoPaneOpen = true;
		window.Shell.FocusActiveList();
		window.Shell.ActiveList.SelectedItems!.Clear();
		window.Shell.ActiveList.SelectedItems.Add(notes);
		await WaitUntil(() => tab.Preview.HasText);
		Render(window, "readme-details");

		// Grid with thumbnails, dark theme
		App.ApplyTheme(AppTheme.Dark);
		vm.IsInfoPaneOpen = false;
		tab.NavigateTo(pics);
		await WaitUntil(() => !tab.IsLoading && tab.Items.Count > 0);
		tab.SetLayoutCommand.Execute(LayoutMode.GridMedium);
		await WaitUntil(() => tab.Items.Where(i => i.IsImage).All(i => i.Thumbnail is not null), 10000);
		Render(window, "readme-grid-dark");

		App.ApplyTheme(AppTheme.Light);
		tab.SetLayoutCommand.Execute(LayoutMode.Details);
		window.Close();
	});
}
