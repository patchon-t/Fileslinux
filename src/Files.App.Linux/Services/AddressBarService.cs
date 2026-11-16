// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Linux.Services;

public enum AddressActionKind
{
	/// <summary>Go to a folder (Path null = Home, or the trash).</summary>
	Navigate,

	/// <summary>Open a file with its default app.</summary>
	OpenFile,

	/// <summary>Open a terminal in the current folder ("cmd", "terminal"...).</summary>
	OpenTerminal,

	/// <summary>Start a desktop (GUI) program in the current folder.</summary>
	Run,

	/// <summary>Run a command-line program in a terminal that stays open, like a console window on Windows.</summary>
	RunInTerminal,

	/// <summary>Nothing matches: "Files can't find ...".</summary>
	NotFound,
}

/// <summary>What typing <c>Text</c> in the address bar and pressing Enter does.</summary>
public sealed record AddressAction(AddressActionKind Kind, string Text, string? Path = null, IReadOnlyList<string>? Command = null);

/// <summary>A suggestion shown under the address bar while typing.</summary>
public sealed record AddressSuggestion(string Text, string Display, string? Detail, AddressActionKind Kind)
{
	public FluentIcons.Common.Symbol Icon => Kind switch
	{
		AddressActionKind.Navigate => FluentIcons.Common.Symbol.Folder,
		AddressActionKind.OpenFile => FluentIcons.Common.Symbol.Document,
		AddressActionKind.OpenTerminal => FluentIcons.Common.Symbol.WindowConsole,
		_ => FluentIcons.Common.Symbol.AppGeneric,
	};

	public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

/// <summary>
/// The address bar works like the one in Windows Explorer and Files: it takes a path to go to or a file to open,
/// and also runs commands in the current folder ("cmd" opens a terminal there, "code ." starts VS Code, "htop" runs
/// in a terminal).
/// </summary>
public sealed class AddressBarService
{
	/// <summary>Words that open a terminal in the current folder (Windows: cmd, powershell, wt).</summary>
	public static readonly IReadOnlySet<string> TerminalAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"cmd", "cmd.exe", "terminal", "console", "powershell", "powershell.exe", "pwsh", "wt", "wt.exe",
	};

	private static readonly HashSet<string> Shells = new(StringComparer.Ordinal) { "sh", "bash", "zsh", "fish", "dash", "ksh", "tcsh", "csh" };

	private readonly Func<string, string?> _findInPath;
	private readonly Func<string, bool> _isDesktopApp;

	/// <param name="findInPath">Finds an executable on $PATH (null if missing).</param>
	/// <param name="isDesktopApp">True when an executable is a graphical app (it has a .desktop entry that doesn't need a terminal).</param>
	public AddressBarService(Func<string, string?>? findInPath = null, Func<string, bool>? isDesktopApp = null)
	{
		_findInPath = findInPath ?? LauncherService.FindInPath;
		_isDesktopApp = isDesktopApp ?? LauncherService.IsDesktopApp;
	}

	public static AddressBarService Default { get; } = new();

	/// <summary>Decides what Enter does for <paramref name="input"/>, typed while <paramref name="currentFolder"/> is open.</summary>
	public AddressAction Resolve(string input, string? currentFolder)
	{
		var text = input.Trim();
		var folder = currentFolder is { Length: > 0 } c && Directory.Exists(c) ? c : XdgPaths.Home;
		if (text.Length == 0 || text.Equals("home", StringComparison.OrdinalIgnoreCase))
			return new(AddressActionKind.Navigate, text);
		if (text.StartsWith("trash:", StringComparison.OrdinalIgnoreCase))
			return new(AddressActionKind.Navigate, text, TrashService.TrashUri);
		if (TerminalAliases.Contains(text))
			return new(AddressActionKind.OpenTerminal, text, folder);

		// Paths: absolute, home-relative, relative, file:// URLs, or a name in the current folder.
		var looksLikePath = text.StartsWith('/') || text == "~" || text.StartsWith("~/", StringComparison.Ordinal)
			|| text == "." || text == ".." || text.StartsWith("./", StringComparison.Ordinal) || text.StartsWith("../", StringComparison.Ordinal)
			|| text.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
		if (ToPath(text, folder) is { } path)
		{
			if (Directory.Exists(path))
				return new(AddressActionKind.Navigate, text, path);
			if (File.Exists(path) && looksLikePath && IsRunnableFile(path))
			{
				// ./build.sh or ~/Apps/Tool.AppImage runs the program, like typing an .exe path on Windows.
				return path.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase)
					? new(AddressActionKind.Run, text, folder, [path])
					: new(AddressActionKind.RunInTerminal, text, folder, [path]);
			}
			if (File.Exists(path) && (looksLikePath || !IsExecutableName(text)))
				return new(AddressActionKind.OpenFile, text, path);
		}
		if (looksLikePath && !text.Contains(' '))
			return new(AddressActionKind.NotFound, text);

		// Commands, run in the current folder.
		var tokens = LauncherService.SplitCommand(text);
		if (tokens.Count == 0)
			return new(AddressActionKind.NotFound, text);
		var program = tokens[0];
		var executable = program.Contains('/') ? ToPath(program, folder) is { } p && File.Exists(p) && IsExecutable(p) ? p : null : _findInPath(program);
		if (executable is null)
			return new(AddressActionKind.NotFound, text);
		if (Shells.Contains(Path.GetFileName(executable)) && tokens.Count == 1)
			return new(AddressActionKind.OpenTerminal, text, folder);
		var command = new List<string>(tokens) { [0] = executable };
		return _isDesktopApp(executable)
			? new(AddressActionKind.Run, text, folder, command)
			: new(AddressActionKind.RunInTerminal, text, folder, command);
	}

	/// <summary>Turns typed text into a full path (~, relative paths and file:// URLs are understood).</summary>
	internal static string? ToPath(string text, string folder)
	{
		try
		{
			if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
				return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri.LocalPath : null;
			if (text == "~")
				return XdgPaths.Home;
			if (text.StartsWith("~/", StringComparison.Ordinal))
				text = System.IO.Path.Combine(XdgPaths.Home, text[2..]);
			var full = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(text) ? text : System.IO.Path.Combine(folder, text));
			return full.Length > 1 ? full.TrimEnd('/') : full;
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>A bare word that is also a program name (e.g. "htop") runs the program rather than opening a same-named file.</summary>
	private bool IsExecutableName(string text) => !text.Contains('/') && !text.Contains(' ') && _findInPath(text) is not null;

	/// <summary>Executable files that are programs or scripts (not documents that merely have the execute bit).</summary>
	private static bool IsRunnableFile(string path)
	{
		if (!IsExecutable(path))
			return false;
		var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
		return ext is "" or ".sh" or ".bash" or ".py" or ".pl" or ".rb" or ".appimage" or ".run" or ".bin";
	}

	private static bool IsExecutable(string path)
	{
		try
		{
			return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>Suggestions for what is being typed: matching folders and files, then commands.</summary>
	public IReadOnlyList<AddressSuggestion> Suggest(string input, string? currentFolder, bool showHidden, int max = 8)
	{
		var text = input.TrimStart();
		var folder = currentFolder is { Length: > 0 } c && Directory.Exists(c) ? c : XdgPaths.Home;
		var result = new List<AddressSuggestion>();
		if (text.Length == 0)
			return result;

		// Folder and file names: complete the last part of what was typed.
		var slash = text.LastIndexOf('/');
		var parentText = slash >= 0 ? text[..(slash + 1)] : string.Empty;
		var prefix = slash >= 0 ? text[(slash + 1)..] : text;
		var parent = parentText.Length == 0 ? folder : ToPath(parentText.TrimEnd('/') is { Length: > 0 } t ? t : "/", folder);
		if (parent is not null && Directory.Exists(parent) && !text.Contains(' '))
		{
			try
			{
				var entries = new DirectoryInfo(parent).EnumerateFileSystemInfos(prefix + "*", new EnumerationOptions { IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive })
					.Where(e => showHidden || !e.Name.StartsWith('.') || prefix.StartsWith('.'))
					.OrderBy(e => e is FileInfo)
					.ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
					.Take(max);
				foreach (var e in entries)
				{
					var isDir = e is DirectoryInfo;
					result.Add(new AddressSuggestion(parentText + e.Name + (isDir ? "/" : string.Empty), e.Name, isDir ? null : "Open", isDir ? AddressActionKind.Navigate : AddressActionKind.OpenFile));
				}
			}
			catch (Exception)
			{
				// Unreadable folder: no suggestions from it.
			}
		}

		// Commands (only for a plain first word).
		if (slash < 0 && !text.Contains(' '))
		{
			foreach (var alias in new[] { "cmd", "terminal" })
			{
				if (alias.StartsWith(text, StringComparison.OrdinalIgnoreCase) && result.Count < max)
					result.Add(new AddressSuggestion(alias, alias, "Open a terminal here", AddressActionKind.OpenTerminal));
			}
			foreach (var program in LauncherService.ExecutablesStartingWith(text, max - result.Count))
				result.Add(new AddressSuggestion(program, program, "Run", AddressActionKind.Run));
		}
		return result.Take(max).ToList();
	}
}
