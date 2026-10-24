// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Headless;
using Files.App.Linux.Services;

[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.ClassLevel)]

namespace Files.App.Linux.Tests;

/// <summary>Sandboxes HOME/XDG directories so tests never touch the real user profile.</summary>
[TestClass]
public static class TestEnvironment
{
	public static string Root { get; private set; } = string.Empty;

	public static HeadlessUnitTestSession? Session { get; private set; }

	[AssemblyInitialize]
	public static void Init(TestContext _)
	{
		Root = Path.Combine(Path.GetTempPath(), "files-linux-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
		// FILES_DEMO_HOME lets the README screenshots show a realistic path such as /home/alex.
		var home = Environment.GetEnvironmentVariable("FILES_DEMO_HOME") is { Length: > 0 } demo ? demo : Path.Combine(Root, "alex");
		Directory.CreateDirectory(home);
		foreach (var d in new[] { "Desktop", "Downloads", "Documents", "Pictures", "Music", "Videos" })
			Directory.CreateDirectory(Path.Combine(home, d));
		Environment.SetEnvironmentVariable("HOME", home);
		Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(home, ".config"));
		Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(home, ".local", "share"));
		SettingsService.ConfigDirectoryOverride = Path.Combine(home, ".config", "files");
	}

	public static HeadlessUnitTestSession GetSession() => Session ??= HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

	[AssemblyCleanup]
	public static void Cleanup()
	{
		Session?.Dispose();
		try
		{
			Directory.Delete(Root, true);
		}
		catch
		{
			// Best effort.
		}
	}

	public static string NewTempDir()
	{
		var dir = Path.Combine(Root, "work", Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(dir);
		return dir;
	}
}

public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
		.UseSkia()
		.WithInterFont()
		.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
