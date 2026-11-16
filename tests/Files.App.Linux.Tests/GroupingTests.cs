// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Files.App.Linux.Models;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using Files.App.Linux.Views;

namespace Files.App.Linux.Tests;

[TestClass]
public sealed class GroupingTests
{
	private static FileItem Item(string name, DateTime modified, long size = 10, bool dir = false) => new()
	{
		Name = name,
		FullPath = "/x/" + name,
		IsDirectory = dir,
		Size = size,
		Modified = modified,
		Created = modified,
	};

	[TestMethod]
	public void DateGroups_MatchWindows()
	{
		var now = new DateTime(2026, 11, 18, 15, 0, 0);
		string Title(DateTime d) => GroupingService.GroupOf(Item("f", d), GroupColumn.DateModified, now).Title;
		Assert.AreEqual("Today", Title(now.AddHours(-2)));
		Assert.AreEqual("Yesterday", Title(now.AddDays(-1)));
		Assert.AreEqual("Last month", Title(new DateTime(2026, 10, 3)));
		Assert.AreEqual("Earlier this year", Title(new DateTime(2026, 2, 14)));
		Assert.AreEqual("Last year", Title(new DateTime(2025, 6, 1)));
		Assert.AreEqual("A long time ago", Title(new DateTime(2019, 6, 1)));
	}

	[TestMethod]
	public void Build_OrdersGroupsCountsAndCollapses()
	{
		var now = new DateTime(2026, 11, 18, 15, 0, 0);
		var items = new[]
		{
			Item("today.txt", now),
			Item("old.txt", new DateTime(2019, 1, 1)),
			Item("feb.txt", new DateTime(2026, 2, 1)),
			Item("today2.txt", now.AddMinutes(-5)),
		};
		var display = GroupingService.Build(items, GroupColumn.DateModified, new HashSet<string>(), now);
		var headers = display.OfType<GroupHeader>().ToList();
		CollectionAssert.AreEqual(new[] { "Today", "Earlier this year", "A long time ago" }, headers.Select(h => h.Title).ToArray(), "Newest group first.");
		Assert.AreEqual(2, headers[0].Count);
		Assert.AreSame(items[0], display[1], "Items keep their order inside the group.");

		var collapsed = GroupingService.Build(items, GroupColumn.DateModified, new HashSet<string> { "date:today" }, now);
		Assert.IsFalse(collapsed.Contains(items[0]), "A collapsed group hides its items.");
		Assert.IsTrue(collapsed.OfType<GroupHeader>().First().IsCollapsed);
		Assert.AreEqual(2, collapsed.OfType<GroupHeader>().First().Count, "The header still shows how many items it holds.");

		Assert.AreEqual(4, GroupingService.Build(items, GroupColumn.None, new HashSet<string>(), now).Count, "No grouping: just the items.");
	}

	[TestMethod]
	public void OtherGroupings()
	{
		var now = DateTime.Now;
		Assert.AreEqual("Folders", GroupingService.GroupOf(Item("d", now, dir: true), GroupColumn.Size, now).Title);
		Assert.AreEqual("Tiny (0 – 16 KB)", GroupingService.GroupOf(Item("a", now, 2000), GroupColumn.Size, now).Title);
		Assert.AreEqual("Medium (1 – 128 MB)", GroupingService.GroupOf(Item("a", now, 5_000_000), GroupColumn.Size, now).Title);
		Assert.AreEqual("B", GroupingService.GroupOf(Item("budget.xlsx", now), GroupColumn.Name, now).Title);
		Assert.AreEqual("0–9", GroupingService.GroupOf(Item("2024.txt", now), GroupColumn.Name, now).Title);
		Assert.AreEqual("PDF document", GroupingService.GroupOf(Item("a.pdf", now), GroupColumn.Type, now).Title);
	}

	[TestMethod]
	public Task Downloads_IsGroupedByDate_OtherFoldersAreNot() => TestEnvironment.GetSession().Dispatch(async () =>
	{
		FolderViewService.ResetAll();
		SettingsService.Current.EnableAnimations = false;
		App.ApplyMotion(false);
		var downloads = XdgPaths.GetUserDir("DOWNLOAD");
		Directory.CreateDirectory(downloads);
		foreach (var f in Directory.GetFiles(downloads))
			File.Delete(f);
		void Make(string name, DateTime when)
		{
			var p = Path.Combine(downloads, name);
			File.WriteAllText(p, name);
			File.SetLastWriteTime(p, when);
		}
		Make("new-report.pdf", DateTime.Now.AddMinutes(-10));
		Make("photo.jpg", DateTime.Now.AddMinutes(-30));
		Make("installer.deb", new DateTime(DateTime.Now.Year - 1, 3, 1));
		Make("ancient.zip", new DateTime(2015, 5, 5));

		var vm = new MainWindowViewModel(watchMounts: false);
		var window = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
		vm.Initialize([downloads]);
		window.Show();
		try
		{
			var tab = vm.SelectedTab!;
			var start = Environment.TickCount64;
			while (tab.IsLoading || tab.Items.Count < 4)
			{
				Dispatcher.UIThread.RunJobs();
				await Task.Delay(10);
				if (Environment.TickCount64 - start > 5000)
					Assert.Fail("Downloads did not load.");
			}

			// Downloads: grouped by date modified, newest first, like Windows.
			Assert.AreEqual(GroupColumn.DateModified, tab.GroupBy);
			Assert.AreEqual(SortColumn.DateModified, tab.SortBy);
			Assert.IsTrue(tab.SortDescending);
			var first = (GroupHeader)tab.DisplayItems[0];
			Assert.AreEqual("Today", first.Title);
			Assert.AreEqual("new-report.pdf", ((FileItem)tab.DisplayItems[1]).Name, "Newest first.");
			CollectionAssert.AreEqual(new[] { "Today", "Last year", "A long time ago" }, tab.DisplayItems.OfType<GroupHeader>().Select(h => h.Title).ToArray());

			// Clicking a header collapses its group; clicking again expands it.
			for (int i = 0; i < 5; i++)
			{
				Dispatcher.UIThread.RunJobs();
				AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			}
			var headerContainer = window.Shell.ActiveList.ContainerFromIndex(0)!;
			Assert.IsTrue(headerContainer.Classes.Contains("groupheader"));
			var p = headerContainer.TranslatePoint(new Point(60, 18), window)!.Value;
			window.MouseDown(p, MouseButton.Left);
			window.MouseUp(p, MouseButton.Left);
			Dispatcher.UIThread.RunJobs();
			Assert.IsTrue(first.IsCollapsed);
			Assert.IsFalse(tab.DisplayItems.OfType<FileItem>().Any(i => i.Name == "new-report.pdf"));
			Assert.IsEmpty(tab.SelectedItems, "Headers are never selected.");
			tab.ToggleGroup(first);
			Assert.IsTrue(tab.DisplayItems.OfType<FileItem>().Any(i => i.Name == "new-report.pdf"));
			Render(window, "downloads-grouped");

			// Another folder is not grouped by default.
			var other = TestEnvironment.NewTempDir();
			File.WriteAllText(Path.Combine(other, "a.txt"), "a");
			tab.NavigateTo(other);
			start = Environment.TickCount64;
			while (tab.IsLoading || tab.CurrentPath != other)
			{
				Dispatcher.UIThread.RunJobs();
				await Task.Delay(10);
				if (Environment.TickCount64 - start > 5000)
					Assert.Fail("Folder did not load.");
			}
			Assert.AreEqual(GroupColumn.None, tab.GroupBy);
			Assert.IsFalse(tab.DisplayItems.OfType<GroupHeader>().Any());

			// Grouping can be turned on for one folder; it is remembered for that folder only.
			tab.SetGroupCommand.Execute(GroupColumn.Type);
			Assert.IsTrue(tab.DisplayItems.OfType<GroupHeader>().Any());
			Assert.AreEqual(GroupColumn.Type, FolderViewService.Get(other).GroupBy);
			Assert.AreEqual(GroupColumn.None, FolderViewService.Get(TestEnvironment.NewTempDir()).GroupBy);
		}
		finally
		{
			FolderViewService.ResetAll();
			SettingsService.Current.EnableAnimations = true;
			App.ApplyMotion(true);
			window.Close();
		}
		return true;
	}, CancellationToken.None);

	private static void Render(Avalonia.Controls.Window window, string name)
	{
		if (Environment.GetEnvironmentVariable("FILES_SCREENSHOT_DIR") is not { } dir)
			return;
		Dispatcher.UIThread.RunJobs();
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		if (window.CaptureRenderedFrame() is { } frame)
		{
			Directory.CreateDirectory(dir);
			frame.Save(Path.Combine(dir, name + ".png"));
		}
	}
}
