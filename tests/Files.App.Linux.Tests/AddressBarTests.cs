// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Files.App.Linux.Services;
using Files.App.Linux.ViewModels;
using Files.App.Linux.Views;

namespace Files.App.Linux.Tests;

[TestClass]
public sealed class AddressBarTests
{
	private static AddressBarService Service(params string[] desktopApps) => new(
		name => name switch
		{
			"code" => "/usr/bin/code",
			"htop" => "/usr/bin/htop",
			"ls" => "/usr/bin/ls",
			"bash" => "/usr/bin/bash",
			_ => null,
		},
		exe => desktopApps.Contains(Path.GetFileName(exe)));

	private static string Folder()
	{
		var dir = TestEnvironment.NewTempDir();
		Directory.CreateDirectory(Path.Combine(dir, "Documents"));
		File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
		File.WriteAllText(Path.Combine(dir, "htop"), "a file named like a program");
		var script = Path.Combine(dir, "build.sh");
		File.WriteAllText(script, "#!/bin/sh\necho hi\n");
		File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return dir;
	}

	[TestMethod]
	public void Resolve_PathsFilesAndCommands()
	{
		var dir = Folder();
		var s = Service("code");

		Assert.AreEqual(AddressActionKind.Navigate, s.Resolve("", dir).Kind);
		Assert.IsNull(s.Resolve("", dir).Path, "Empty goes Home.");
		Assert.AreEqual(TrashService.TrashUri, s.Resolve("trash://", dir).Path);

		// "cmd" opens a terminal in the current folder, like Windows.
		foreach (var word in new[] { "cmd", "CMD", "terminal", "powershell", "wt" })
		{
			var a = s.Resolve(word, dir);
			Assert.AreEqual(AddressActionKind.OpenTerminal, a.Kind, word);
			Assert.AreEqual(dir, a.Path);
		}
		Assert.AreEqual(AddressActionKind.OpenTerminal, s.Resolve("bash", dir).Kind, "A bare shell opens a terminal.");

		// Paths.
		Assert.AreEqual(Path.Combine(dir, "Documents"), s.Resolve("Documents", dir).Path, "A name in the current folder.");
		Assert.AreEqual(Path.Combine(dir, "Documents"), s.Resolve("./Documents/", dir).Path);
		Assert.AreEqual(XdgPaths.Home, s.Resolve("~", dir).Path);
		Assert.AreEqual(dir, s.Resolve("Documents/..", dir).Path);
		Assert.AreEqual(Path.Combine(dir, "Documents"), s.Resolve(new Uri(Path.Combine(dir, "Documents")).AbsoluteUri, dir).Path);
		Assert.AreEqual(AddressActionKind.NotFound, s.Resolve("/no/such/place", dir).Kind);

		// Files open with their app; scripts run.
		var file = s.Resolve("notes.txt", dir);
		Assert.AreEqual(AddressActionKind.OpenFile, file.Kind);
		Assert.AreEqual(Path.Combine(dir, "notes.txt"), file.Path);
		var script = s.Resolve("./build.sh", dir);
		Assert.AreEqual(AddressActionKind.RunInTerminal, script.Kind);
		Assert.AreEqual(Path.Combine(dir, "build.sh"), script.Command![0]);

		// Programs: GUI apps start directly, command-line tools run in a terminal; both in the current folder.
		var code = s.Resolve("code .", dir);
		Assert.AreEqual(AddressActionKind.Run, code.Kind);
		CollectionAssert.AreEqual(new[] { "/usr/bin/code", "." }, code.Command!.ToArray());
		Assert.AreEqual(dir, code.Path);
		var ls = s.Resolve("ls -la \"My Folder\"", dir);
		Assert.AreEqual(AddressActionKind.RunInTerminal, ls.Kind);
		CollectionAssert.AreEqual(new[] { "/usr/bin/ls", "-la", "My Folder" }, ls.Command!.ToArray());
		Assert.AreEqual(AddressActionKind.RunInTerminal, s.Resolve("htop", dir).Kind, "A program name wins over a same-named file.");
		Assert.AreEqual(AddressActionKind.NotFound, s.Resolve("nosuchprogram --help", dir).Kind);
	}

	[TestMethod]
	public void TerminalArguments_MatchEachTerminal()
	{
		string[] cmd = ["sh", "-c", "ls"];
		CollectionAssert.AreEqual(new[] { "-e", "sh", "-c", "ls" }, LauncherService.TerminalArguments("x-terminal-emulator", cmd).ToArray());
		CollectionAssert.AreEqual(new[] { "--", "sh", "-c", "ls" }, LauncherService.TerminalArguments("/usr/bin/gnome-terminal", cmd).ToArray());
		CollectionAssert.AreEqual(new[] { "-x", "sh", "-c", "ls" }, LauncherService.TerminalArguments("xfce4-terminal", cmd).ToArray());
		CollectionAssert.AreEqual(new[] { "sh", "-c", "ls" }, LauncherService.TerminalArguments("kitty", cmd).ToArray());
		Assert.AreEqual("'it'\\''s'", LauncherService.ShellQuote("it's"));
	}

	[TestMethod]
	public void Suggest_FoldersFilesAndCommands()
	{
		var dir = Folder();
		var s = Service();
		var docs = s.Suggest("Doc", dir, showHidden: false);
		Assert.AreEqual("Documents/", docs[0].Text);
		Assert.AreEqual(AddressActionKind.Navigate, docs[0].Kind);
		Assert.IsTrue(s.Suggest("no", dir, false).Any(x => x.Text == "notes.txt" && x.Kind == AddressActionKind.OpenFile));
		Assert.IsTrue(s.Suggest("cm", dir, false).Any(x => x.Text == "cmd" && x.Kind == AddressActionKind.OpenTerminal));
		var deeper = s.Suggest(dir + "/Doc", dir, false);
		Assert.AreEqual(dir + "/Documents/", deeper[0].Text);
	}
}

[TestClass]
public sealed class AddressBarUiTests
{
	/// <summary>A stand-in terminal that records its working folder and arguments, then exits.</summary>
	private static (string Terminal, string Log) FakeTerminal()
	{
		var dir = TestEnvironment.NewTempDir();
		var log = Path.Combine(dir, "terminal.log");
		var term = Path.Combine(dir, "fake-term");
		File.WriteAllText(term, $"#!/bin/sh\n{{ pwd; for a in \"$@\"; do echo \"[$a]\"; done; }} > '{log}'\n");
		File.SetUnixFileMode(term, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return (term, log);
	}

	private static async Task WaitFor(Func<bool> condition, int timeoutMs = 5000)
	{
		var start = Environment.TickCount64;
		while (!condition())
		{
			if (Environment.TickCount64 - start > timeoutMs)
				Assert.Fail("Timed out.");
			Dispatcher.UIThread.RunJobs();
			AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			await Task.Delay(20);
		}
	}

	[TestMethod]
	public Task AddressBar_EditsSuggestsAndRunsCommands() => TestEnvironment.GetSession().Dispatch(async () =>
	{
		var root = TestEnvironment.NewTempDir();
		Directory.CreateDirectory(Path.Combine(root, "Projects"));
		var (terminal, log) = FakeTerminal();
		SettingsService.Current.TerminalCommand = terminal;
		var vm = new MainWindowViewModel(watchMounts: false);
		var window = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
		vm.Initialize([root]);
		window.Show();
		try
		{
			var tab = vm.SelectedTab!;
			await WaitFor(() => !tab.IsLoading);

			// Clicking the empty part of the address bar starts editing, with the path selected. The path here is
			// long enough to fill the bar, so this is the strip kept free at its right end.
			var omnibar = window.Omnibar;
			var empty = omnibar.TranslatePoint(new Point(omnibar.Bounds.Width - 30, omnibar.Bounds.Height / 2), window)!.Value;
			window.MouseDown(empty, MouseButton.Left);
			window.MouseUp(empty, MouseButton.Left);
			Dispatcher.UIThread.RunJobs();
			Assert.IsTrue(window.PathBox.IsVisible, "The address bar is editable.");
			Assert.AreEqual(root, window.PathBox.Text);

			// Typing shows suggestions; Enter on the selected one goes there.
			window.PathBox.Text = "Proj";
			Dispatcher.UIThread.RunJobs();
			Assert.IsTrue(window.SuggestionPopup.IsOpen, "Suggestions appear while typing.");
			window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
			window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
			await WaitFor(() => tab.CurrentPath == Path.Combine(root, "Projects"));
			Assert.IsFalse(window.PathBox.IsVisible);

			// "cmd" opens a terminal in the current folder.
			window.ExecuteAddress("cmd");
			await WaitFor(() => File.Exists(log) && File.ReadAllText(log).Length > 0);
			Assert.AreEqual(Path.Combine(root, "Projects"), File.ReadAllLines(log)[0]);

			// A command-line tool runs in a terminal (that stays open) in the current folder.
			File.Delete(log);
			window.ExecuteAddress("ls -la");
			await WaitFor(() => File.Exists(log) && File.ReadAllText(log).Length > 0);
			var lines = File.ReadAllLines(log);
			Assert.AreEqual(Path.Combine(root, "Projects"), lines[0]);
			CollectionAssert.Contains(lines, "[-e]");
			Assert.IsTrue(lines.Any(l => l.Contains("ls") && l.Contains("-la") && l.Contains("exec")), string.Join("\n", lines));

			// Unknown text shows "Files can't find ..." and returns to the address bar.
			window.ExecuteAddress("nosuchprogram123");
			Assert.AreEqual(AddressActionKind.NotFound, window.LastAddressAction!.Kind);
			await WaitFor(() => window.DialogLayer.IsVisible);
			window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
			await WaitFor(() => window.PathBox.IsVisible && window.PathBox.Text == "nosuchprogram123");
		}
		finally
		{
			SettingsService.Current.TerminalCommand = null;
			window.Close();
		}
		return true;
	}, CancellationToken.None);
}
