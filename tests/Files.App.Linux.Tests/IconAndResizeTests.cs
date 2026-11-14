// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Files.App.Linux.Controls;
using Files.App.Linux.Models;
using Files.App.Linux.Services;
using Files.App.Linux.Services.Icons;
using Files.App.Linux.ViewModels;
using Files.App.Linux.Views;

namespace Files.App.Linux.Tests;

[TestClass]
public sealed class IconTests
{
	private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 48 48\"><rect width=\"48\" height=\"48\" fill=\"#ff0000\"/></svg>";

	/// <summary>Creates a small icon theme in ~/.local/share/icons (the test home) that inherits from a base theme.</summary>
	internal static string CreateTestThemes()
	{
		var icons = Path.Combine(XdgPaths.DataHome, "icons");
		var baseTheme = Path.Combine(icons, "TestBase");
		Directory.CreateDirectory(Path.Combine(baseTheme, "scalable", "places"));
		File.WriteAllText(Path.Combine(baseTheme, "index.theme"), "[Icon Theme]\nName=Test Base\nDirectories=scalable/places\n\n[scalable/places]\nSize=48\nMinSize=8\nMaxSize=512\nType=Scalable\n");
		File.WriteAllText(Path.Combine(baseTheme, "scalable", "places", "folder.svg"), Svg);
		File.WriteAllText(Path.Combine(baseTheme, "scalable", "places", "user-trash.svg"), Svg);

		var theme = Path.Combine(icons, "TestTheme");
		Directory.CreateDirectory(Path.Combine(theme, "16x16", "places"));
		Directory.CreateDirectory(Path.Combine(theme, "48x48", "places"));
		File.WriteAllText(Path.Combine(theme, "index.theme"), "[Icon Theme]\nName=Test Theme\nInherits=TestBase\nDirectories=16x16/places,48x48/places\n\n[16x16/places]\nSize=16\nType=Fixed\n\n[48x48/places]\nSize=48\nType=Fixed\n");
		File.WriteAllText(Path.Combine(theme, "16x16", "places", "user-home.svg"), Svg);
		File.WriteAllText(Path.Combine(theme, "48x48", "places", "user-home.svg"), Svg);
		IconTheme.ClearCache();
		return icons;
	}

	[TestMethod]
	public void MimeDatabase_MapsExtensionsAndIconNames()
	{
		var dir = TestEnvironment.NewTempDir();
		Directory.CreateDirectory(Path.Combine(dir, "mime"));
		File.WriteAllText(Path.Combine(dir, "mime", "globs2"), "# comment\n50:application/pdf:*.pdf\n50:application/gzip:*.gz\n55:application/x-compressed-tar:*.tar.gz\n");
		File.WriteAllText(Path.Combine(dir, "mime", "generic-icons"), "application/pdf:x-office-document\n");
		var db = MimeDatabase.Load([dir]);
		Assert.AreEqual("application/pdf", db.MimeTypeFor("Report.PDF"));
		Assert.AreEqual("application/x-compressed-tar", db.MimeTypeFor("backup.tar.gz"), "The longest extension wins.");
		CollectionAssert.AreEqual(new[] { "application-pdf", "x-office-document", "application-x-generic" }, db.IconNamesFor("application/pdf").ToArray());
	}

	[TestMethod]
	public void IconTheme_LooksUpSizesAndInherits()
	{
		CreateTestThemes();
		var theme = IconTheme.Find("TestTheme");
		Assert.IsNotNull(theme);
		Assert.AreEqual("Test Theme", theme.DisplayName);
		StringAssert.Contains(theme.Lookup(["user-home"], 16), Path.Combine("16x16", "places"));
		StringAssert.Contains(theme.Lookup(["user-home"], 40), Path.Combine("48x48", "places"), "The closest size is used.");
		StringAssert.Contains(theme.Lookup(["no-such-icon", "folder"], 32), Path.Combine("TestBase", "scalable"), "Inherited themes are searched.");
		Assert.IsNull(theme.Lookup(["no-such-icon"], 32));
		Assert.IsTrue(IconTheme.Installed().Any(t => t.Name == "TestTheme"));
	}

	[TestMethod]
	public Task Svg_RendersToTheRequestedSize() => TestEnvironment.GetSession().Dispatch(() =>
	{
		var file = Path.Combine(TestEnvironment.NewTempDir(), "icon.svg");
		File.WriteAllText(file, Svg);
		var bitmap = IconService.RenderSvg(file, 64);
		Assert.IsNotNull(bitmap);
		Assert.AreEqual(64, bitmap.PixelSize.Width);
		Assert.AreEqual(64, bitmap.PixelSize.Height);
		return true;
	}, CancellationToken.None);

	[TestMethod]
	public void IconPack_ExampleImportAndSafety()
	{
		var folder = IconPack.CreateExample();
		var pack = IconPack.Load(folder);
		Assert.IsNotNull(pack);
		Assert.AreEqual("Example pack", pack.DisplayName);
		StringAssert.EndsWith(pack.Resolve([".pdf", "file"]), "pdf.svg");
		StringAssert.EndsWith(pack.Resolve([".docx", "file"]), "file.svg", "Unknown types use the pack's file icon.");

		// Round-trip through a .zip, as people would share a pack.
		var zip = Path.Combine(TestEnvironment.NewTempDir(), "My Pack.zip");
		ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
		var imported = IconPack.Import(zip);
		Assert.AreEqual("Example pack", imported.DisplayName);
		Assert.IsTrue(IconPack.Installed().Any(p => p.Id == imported.Id));

		// A pack can't point outside its own folder.
		var evil = Path.Combine(IconPack.PacksDirectory, "evil");
		Directory.CreateDirectory(evil);
		File.WriteAllText(Path.Combine(evil, "icons.json"), "{ \"name\": \"Evil\", \"icons\": { \"file\": \"../../../../etc/passwd\" } }");
		Assert.IsNull(IconPack.Load(evil)!.Resolve(["file"]));

		Assert.ThrowsExactly<InvalidDataException>(() => IconPack.Import(Path.Combine(TestEnvironment.NewTempDir(), "nothing.txt")));
	}

	[TestMethod]
	public void IconPack_InstallsAsSystemIconTheme()
	{
		var pack = IconPack.Load(IconPack.CreateExample())!;
		var mime = MimeDatabase.Load([]);
		mime.Add(".pdf", "application/pdf");
		var iconsDir = TestEnvironment.NewTempDir();
		var name = pack.InstallAsSystemTheme("TestBase", mime, iconsDir);

		var root = Path.Combine(iconsDir, name);
		var index = File.ReadAllText(Path.Combine(root, "index.theme"));
		StringAssert.Contains(index, "Inherits=TestBase,Adwaita,hicolor");
		Assert.IsTrue(File.Exists(Path.Combine(root, "scalable", "places", "folder.svg")));
		Assert.IsTrue(File.Exists(Path.Combine(root, "scalable", "places", "user-home.svg")));
		Assert.IsTrue(File.Exists(Path.Combine(root, "scalable", "places", "user-trash.svg")));
		Assert.IsTrue(File.Exists(Path.Combine(root, "scalable", "mimetypes", "application-pdf.svg")));
		Assert.IsTrue(File.Exists(Path.Combine(root, "scalable", "mimetypes", "image-x-generic.svg")));

		// Other apps resolve icons from it like any theme.
		var theme = IconTheme.Load(name, [iconsDir]);
		Assert.IsNotNull(theme);
		StringAssert.EndsWith(theme.LookupHere("folder", 32), "folder.svg");
	}

	[TestMethod]
	public Task IconStyle_ChangesIconsInTheApp() => TestEnvironment.GetSession().Dispatch(async () =>
	{
		CreateTestThemes();
		var root = TestEnvironment.NewTempDir();
		Directory.CreateDirectory(Path.Combine(root, "Folder"));
		var vm = new MainWindowViewModel(watchMounts: false);
		var window = new MainWindow { DataContext = vm, Width = 1000, Height = 600 };
		vm.Initialize([root]);
		window.Show();
		try
		{
			var tab = vm.SelectedTab!;
			var start = Environment.TickCount64;
			while (tab.IsLoading || tab.Items.Count == 0)
			{
				Dispatcher.UIThread.RunJobs();
				await Task.Delay(10);
				if (Environment.TickCount64 - start > 5000)
					Assert.Fail("Folder did not load.");
			}
			ItemIcon FolderIcon() => window.Shell.ActiveList.GetRealizedContainers().First().GetVisualDescendants().OfType<ItemIcon>().First();
			Dispatcher.UIThread.RunJobs();
			AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			Assert.IsNotInstanceOfType<Image>(FolderIcon().Content, "Built-in icons by default.");

			IconService.Apply("theme:TestTheme");
			Assert.IsInstanceOfType<Image>(FolderIcon().Content, "The theme's folder icon is shown.");
			Assert.IsTrue(vm.Sidebar.Items.First(i => i.Kind == SidebarItemKind.Home).HasCustomIcon, "The sidebar uses the theme too.");

			IconService.Apply("pack:example-pack");
			Assert.IsInstanceOfType<Image>(FolderIcon().Content, "The pack's folder icon is shown.");

			IconService.Apply("builtin");
			Assert.IsNotInstanceOfType<Image>(FolderIcon().Content);
			Assert.IsFalse(vm.Sidebar.Items.First(i => i.Kind == SidebarItemKind.Home).HasCustomIcon);
		}
		finally
		{
			IconService.Apply("builtin");
			window.Close();
		}
		return true;
	}, CancellationToken.None);
}

[TestClass]
public sealed class ResizeTests
{
	[TestMethod]
	public Task DetailsColumnsAndDetailsPane_Resize() => TestEnvironment.GetSession().Dispatch(async () =>
	{
		SettingsService.Current.EnableAnimations = false;
		App.ApplyMotion(false);
		FolderViewService.ResetAll();
		var root = TestEnvironment.NewTempDir();
		File.WriteAllText(Path.Combine(root, "a.txt"), "a");
		var vm = new MainWindowViewModel(watchMounts: false);
		var window = new MainWindow { DataContext = vm, Width = 1200, Height = 700 };
		vm.Initialize([root]);
		window.Show();
		try
		{
			for (int i = 0; i < 20; i++)
			{
				Dispatcher.UIThread.RunJobs();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
				await Task.Delay(10);
			}

			// Column resize: a drag keeps going (the grip is not rebuilt mid-drag) and the width is remembered.
			var header = window.Shell.FindControl<DetailsRowPanel>("DetailsHeader")!;
			var nameCell = header.Children[DetailsColumns.Name];
			var grip = nameCell.GetVisualDescendants().OfType<Thumb>().First();
			var before = DetailsColumns.Get(DetailsColumns.Name);
			var gripPoint = grip.TranslatePoint(new Point(3, 16), window)!.Value;
			window.MouseDown(gripPoint, MouseButton.Left);
			window.MouseMove(gripPoint + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
			window.MouseMove(gripPoint + new Vector(60, 0), RawInputModifiers.LeftMouseButton);
			window.MouseUp(gripPoint + new Vector(60, 0), MouseButton.Left);
			Dispatcher.UIThread.RunJobs();
			Assert.AreSame(nameCell, header.Children[DetailsColumns.Name], "The header must not be rebuilt while resizing.");
			Assert.AreEqual(before + 60, DetailsColumns.Get(DetailsColumns.Name), 1, "The whole drag is applied.");
			Assert.AreEqual(before + 60, SettingsService.Current.DetailsColumnWidths![DetailsColumns.Name], 1);

			// Details pane: opens at the saved width and the splitter resizes it.
			vm.IsInfoPaneOpen = true;
			for (int i = 0; i < 5; i++)
			{
				Dispatcher.UIThread.RunJobs();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
			var body = window.Shell.FindControl<Grid>("BodyGrid")!;
			var paneColumn = body.ColumnDefinitions[2];
			Assert.AreEqual(300, paneColumn.ActualWidth, 1);
			var splitter = window.Shell.FindControl<GridSplitter>("InfoSplitter")!;
			var p = splitter.TranslatePoint(new Point(2, 100), window)!.Value;
			window.MouseDown(p, MouseButton.Left);
			window.MouseMove(p - new Vector(40, 0), RawInputModifiers.LeftMouseButton);
			window.MouseMove(p - new Vector(100, 0), RawInputModifiers.LeftMouseButton);
			window.MouseUp(p - new Vector(100, 0), MouseButton.Left);
			for (int i = 0; i < 5; i++)
			{
				Dispatcher.UIThread.RunJobs();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
			Assert.AreEqual(400, paneColumn.ActualWidth, 2, "Dragging the splitter 100 px left widens the pane.");
			Assert.AreEqual(400, SettingsService.Current.InfoPaneWidth, 2, "The new width is remembered.");

			vm.IsInfoPaneOpen = false;
			Dispatcher.UIThread.RunJobs();
			Assert.AreEqual(0, paneColumn.Width.Value);
		}
		finally
		{
			SettingsService.Current.InfoPaneWidth = 300;
			SettingsService.Current.DetailsColumnWidths = null;
			vm.IsInfoPaneOpen = false;
			SettingsService.Current.EnableAnimations = true;
			App.ApplyMotion(true);
			window.Close();
		}
		return true;
	}, CancellationToken.None);
}
