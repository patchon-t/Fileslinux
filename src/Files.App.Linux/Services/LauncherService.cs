// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Diagnostics;

namespace Files.App.Linux.Services;

/// <summary>An installed application from a .desktop entry.</summary>
public sealed record DesktopApp(string Id, string Name, string Exec, string? Icon, IReadOnlyList<string> MimeTypes, bool Terminal);

/// <summary>Opens files with the default or a chosen application, and opens terminals.</summary>
public static class LauncherService
{
	public static void OpenWithDefault(string path) => Start("xdg-open", path);

	public static void OpenInTerminal(string folder)
	{
		var custom = SettingsService.Current.TerminalCommand;
		if (!string.IsNullOrWhiteSpace(custom))
		{
			var parts = SplitCommand(custom);
			StartIn(folder, parts[0], [.. parts.Skip(1)]);
			return;
		}

		// Debian provides x-terminal-emulator through the alternatives system.
		foreach (var term in new[] { "x-terminal-emulator", "gnome-terminal", "kgx", "konsole", "xfce4-terminal", "mate-terminal", "tilix", "alacritty", "kitty", "foot", "xterm" })
		{
			if (FindInPath(term) is not null)
			{
				StartIn(folder, term);
				return;
			}
		}
		throw new InvalidOperationException("No terminal emulator was found. Set one in Settings.");
	}

	public static string? FindInPath(string exe)
	{
		var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
		foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = Path.Combine(dir, exe);
			if (File.Exists(candidate))
				return candidate;
		}
		return null;
	}

	private static void Start(string exe, params string[] args)
	{
		var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
		foreach (var a in args)
			psi.ArgumentList.Add(a);
		using var _ = Process.Start(psi);
	}

	private static void StartIn(string folder, string exe, params string[] args)
	{
		var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = folder };
		foreach (var a in args)
			psi.ArgumentList.Add(a);
		using var _ = Process.Start(psi);
	}

	/// <summary>Runs a command and returns its standard output, or null on failure.</summary>
	public static string? RunAndRead(string exe, params string[] args)
	{
		try
		{
			var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
			foreach (var a in args)
				psi.ArgumentList.Add(a);
			using var p = Process.Start(psi);
			if (p is null)
				return null;
			var output = p.StandardOutput.ReadToEnd();
			p.WaitForExit(5000);
			return p.ExitCode == 0 ? output.Trim() : null;
		}
		catch
		{
			return null;
		}
	}

	public static string? GetMimeType(string path) => RunAndRead("xdg-mime", "query", "filetype", path);

	private static List<DesktopApp>? _apps;

	public static IReadOnlyList<DesktopApp> GetInstalledApps()
	{
		if (_apps is not null)
			return _apps;

		var dirs = new List<string> { Path.Combine(XdgPaths.DataHome, "applications") };
		var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } d ? d : "/usr/local/share:/usr/share";
		dirs.AddRange(dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries).Select(x => Path.Combine(x, "applications")));
		dirs.Add("/var/lib/flatpak/exports/share/applications");
		dirs.Add(Path.Combine(XdgPaths.DataHome, "flatpak", "exports", "share", "applications"));

		var apps = new Dictionary<string, DesktopApp>(StringComparer.Ordinal);
		foreach (var dir in dirs)
		{
			if (!Directory.Exists(dir))
				continue;
			foreach (var file in Directory.EnumerateFiles(dir, "*.desktop", SearchOption.AllDirectories))
			{
				var id = Path.GetRelativePath(dir, file).Replace('/', '-');
				if (apps.ContainsKey(id))
					continue;
				if (ParseDesktopFile(file, id) is { } app)
					apps[id] = app;
			}
		}
		_apps = apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
		return _apps;
	}

	internal static DesktopApp? ParseDesktopFile(string file, string id)
	{
		try
		{
			string? name = null, exec = null, icon = null, mime = null, type = null;
			bool noDisplay = false, hidden = false, terminal = false, inEntry = false;
			foreach (var raw in File.ReadLines(file))
			{
				var line = raw.Trim();
				if (line.StartsWith('['))
				{
					inEntry = line == "[Desktop Entry]";
					continue;
				}
				if (!inEntry)
					continue;
				var eq = line.IndexOf('=');
				if (eq <= 0)
					continue;
				var key = line[..eq].Trim();
				var value = line[(eq + 1)..].Trim();
				switch (key)
				{
					case "Name": name = value; break;
					case "Exec": exec = value; break;
					case "Icon": icon = value; break;
					case "MimeType": mime = value; break;
					case "Type": type = value; break;
					case "NoDisplay": noDisplay = value == "true"; break;
					case "Hidden": hidden = value == "true"; break;
					case "Terminal": terminal = value == "true"; break;
				}
			}
			if (type != "Application" || name is null || exec is null || hidden || noDisplay && mime is null)
				return null;
			return new DesktopApp(id, name, exec, icon, (mime ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries), terminal);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Applications that declare support for the MIME type of <paramref name="path"/>.</summary>
	public static IReadOnlyList<DesktopApp> GetAppsFor(string path)
	{
		var mime = GetMimeType(path);
		var apps = GetInstalledApps();
		if (mime is null)
			return [];
		var group = mime.Split('/')[0] + "/*";
		return apps.Where(a => a.MimeTypes.Contains(mime) || a.MimeTypes.Contains(group)).ToList();
	}

	/// <summary>Launches a .desktop application with the given files, expanding field codes.</summary>
	public static void LaunchApp(DesktopApp app, IReadOnlyList<string> files)
	{
		var tokens = SplitCommand(app.Exec);
		var args = new List<string>();
		bool filesAdded = false;
		foreach (var t in tokens.Skip(1))
		{
			switch (t)
			{
				case "%f":
				case "%u":
					if (files.Count > 0)
						args.Add(t == "%u" ? new Uri(files[0]).AbsoluteUri : files[0]);
					filesAdded = true;
					break;
				case "%F":
				case "%U":
					args.AddRange(t == "%U" ? files.Select(f => new Uri(f).AbsoluteUri) : files);
					filesAdded = true;
					break;
				case "%i":
				case "%c":
				case "%k":
					break;
				default:
					args.Add(t.Replace("%%", "%"));
					break;
			}
		}
		if (!filesAdded)
			args.AddRange(files);

		var exe = tokens[0];
		if (app.Terminal)
		{
			args.Insert(0, exe);
			args.Insert(0, "-e");
			exe = FindInPath("x-terminal-emulator") is not null ? "x-terminal-emulator" : "xterm";
		}
		Start(exe, [.. args]);
	}

	internal static List<string> SplitCommand(string command)
	{
		var result = new List<string>();
		var current = new System.Text.StringBuilder();
		bool inQuotes = false, has = false;
		for (int i = 0; i < command.Length; i++)
		{
			var c = command[i];
			if (c == '\\' && i + 1 < command.Length && inQuotes)
			{
				current.Append(command[++i]);
				continue;
			}
			if (c == '"')
			{
				inQuotes = !inQuotes;
				has = true;
				continue;
			}
			if (char.IsWhiteSpace(c) && !inQuotes)
			{
				if (has || current.Length > 0)
					result.Add(current.ToString());
				current.Clear();
				has = false;
				continue;
			}
			current.Append(c);
		}
		if (has || current.Length > 0)
			result.Add(current.ToString());
		return result;
	}

	/// <summary>Unmounts a removable drive using udisks/gio.</summary>
	public static bool Eject(string mountPoint)
	{
		if (RunAndRead("gio", "mount", "-u", mountPoint) is not null)
			return true;
		return RunAndRead("umount", mountPoint) is not null;
	}

	/// <summary>Gets owner and group names using stat(1).</summary>
	public static (string? Owner, string? Group) GetOwner(string path)
	{
		var output = RunAndRead("stat", "-c", "%U\t%G", path);
		if (output is null)
			return (null, null);
		var parts = output.Split('\t');
		return (parts[0], parts.Length > 1 ? parts[1] : null);
	}
}
