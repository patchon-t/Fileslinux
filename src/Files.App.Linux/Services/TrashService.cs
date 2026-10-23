// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace Files.App.Linux.Services;

/// <summary>An item in a freedesktop.org trash directory.</summary>
public sealed record TrashEntry(string TrashedPath, string InfoPath, string OriginalPath, DateTime DeletionDate);

/// <summary>Implements the freedesktop.org Trash specification (compatible with Nautilus, Dolphin, Thunar, gio trash).</summary>
public sealed class TrashService
{
	public static TrashService Default { get; } = new();

	public const string TrashUri = "trash://";

	private readonly string _homeTrash;
	private readonly Func<IReadOnlyList<MountEntry>> _mounts;

	public TrashService(string? homeTrash = null, Func<IReadOnlyList<MountEntry>>? mounts = null)
	{
		_homeTrash = homeTrash ?? Path.Combine(XdgPaths.DataHome, "Trash");
		_mounts = mounts ?? MountService.GetMounts;
	}

	public string HomeTrash => _homeTrash;

	private static int? _uid;

	public static int Uid
	{
		get
		{
			if (_uid is { } u)
				return u;
			try
			{
				foreach (var line in File.ReadLines("/proc/self/status"))
				{
					if (line.StartsWith("Uid:", StringComparison.Ordinal))
					{
						var parts = line[4..].Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
						_uid = int.Parse(parts[0], CultureInfo.InvariantCulture);
						return _uid.Value;
					}
				}
			}
			catch
			{
				// Fall through.
			}
			_uid = 1000;
			return _uid.Value;
		}
	}

	/// <summary>Moves <paramref name="path"/> to the trash.</summary>
	public TrashEntry Trash(string path)
	{
		var full = Path.GetFullPath(path).TrimEnd('/');
		if (!File.Exists(full) && !Directory.Exists(full) && new FileInfo(full).LinkTarget is null)
			throw new FileNotFoundException("Item not found", full);

		var mounts = _mounts();
		var trashDir = GetTrashDirFor(full, mounts);
		var filesDir = Path.Combine(trashDir, "files");
		var infoDir = Path.Combine(trashDir, "info");
		Directory.CreateDirectory(filesDir);
		Directory.CreateDirectory(infoDir);
		TrySetMode(trashDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		var baseName = Path.GetFileName(full);
		var (name, infoPath) = ReserveInfoFile(infoDir, baseName, full);
		var target = Path.Combine(filesDir, name);

		try
		{
			if (Directory.Exists(full) && new DirectoryInfo(full).LinkTarget is null)
				Directory.Move(full, target);
			else
				File.Move(full, target);
		}
		catch
		{
			File.Delete(infoPath);
			throw;
		}

		return new TrashEntry(target, infoPath, full, DateTime.Now);
	}

	private (string Name, string InfoPath) ReserveInfoFile(string infoDir, string baseName, string originalPath)
	{
		var content = "[Trash Info]\nPath=" + EncodePath(originalPath) + "\nDeletionDate=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "\n";
		var stem = baseName;
		for (int i = 1; i < 10000; i++)
		{
			var name = i == 1 ? stem : $"{stem}.{i}";
			var infoPath = Path.Combine(infoDir, name + ".trashinfo");
			if (File.Exists(Path.Combine(Path.GetDirectoryName(infoDir)!, "files", name)))
				continue;
			try
			{
				using var fs = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write);
				var bytes = Encoding.UTF8.GetBytes(content);
				fs.Write(bytes);
				return (name, infoPath);
			}
			catch (IOException) when (File.Exists(infoPath))
			{
				// Name taken: try the next one.
			}
		}
		throw new IOException("Could not reserve a name in the trash.");
	}

	/// <summary>Chooses the trash directory: home trash on the same mount, otherwise $topdir/.Trash/$uid or $topdir/.Trash-$uid.</summary>
	internal string GetTrashDirFor(string fullPath, IReadOnlyList<MountEntry> mounts)
	{
		var fileMount = MountService.GetMountPoint(fullPath, mounts);
		var homeTrashMount = MountService.GetMountPoint(ExistingAncestor(_homeTrash), mounts);
		if (fileMount == homeTrashMount)
			return _homeTrash;

		var shared = Path.Combine(fileMount, ".Trash");
		try
		{
			var info = new DirectoryInfo(shared);
			if (info.Exists && info.LinkTarget is null && (info.UnixFileMode & UnixFileMode.StickyBit) != 0)
			{
				var dir = Path.Combine(shared, Uid.ToString(CultureInfo.InvariantCulture));
				Directory.CreateDirectory(dir);
				return dir;
			}
		}
		catch
		{
			// Fall back to .Trash-$uid.
		}

		var own = Path.Combine(fileMount, ".Trash-" + Uid.ToString(CultureInfo.InvariantCulture));
		Directory.CreateDirectory(own);
		return own;
	}

	private static string ExistingAncestor(string path)
	{
		var p = path;
		while (!Directory.Exists(p) && Path.GetDirectoryName(p) is { } parent)
			p = parent;
		return p;
	}

	/// <summary>All trash directories that currently exist.</summary>
	public IEnumerable<string> GetTrashDirectories()
	{
		var dirs = new List<string>();
		if (Directory.Exists(_homeTrash))
			dirs.Add(_homeTrash);

		var uid = Uid.ToString(CultureInfo.InvariantCulture);
		foreach (var m in _mounts())
		{
			foreach (var candidate in new[] { Path.Combine(m.MountPoint, ".Trash", uid), Path.Combine(m.MountPoint, ".Trash-" + uid) })
			{
				try
				{
					if (Directory.Exists(Path.Combine(candidate, "files")) && !dirs.Contains(candidate))
						dirs.Add(candidate);
				}
				catch
				{
					// Skip inaccessible mounts.
				}
			}
		}
		return dirs;
	}

	public List<TrashEntry> List()
	{
		var result = new List<TrashEntry>();
		foreach (var trash in GetTrashDirectories())
		{
			var infoDir = Path.Combine(trash, "info");
			var filesDir = Path.Combine(trash, "files");
			if (!Directory.Exists(infoDir))
				continue;

			IEnumerable<string> infos;
			try
			{
				infos = Directory.EnumerateFiles(infoDir, "*.trashinfo").ToList();
			}
			catch
			{
				continue;
			}

			foreach (var info in infos)
			{
				var name = Path.GetFileNameWithoutExtension(info);
				var trashed = Path.Combine(filesDir, name);
				if (!File.Exists(trashed) && !Directory.Exists(trashed) && new FileInfo(trashed).LinkTarget is null)
					continue;

				var (original, date) = ParseInfo(info, trash);
				result.Add(new TrashEntry(trashed, info, original ?? Path.Combine("/", name), date));
			}
		}
		return result;
	}

	internal static (string? Path, DateTime Date) ParseInfo(string infoFile, string trashDir)
	{
		string? path = null;
		DateTime date = DateTime.MinValue;
		try
		{
			foreach (var line in File.ReadLines(infoFile))
			{
				if (line.StartsWith("Path=", StringComparison.Ordinal))
				{
					path = Uri.UnescapeDataString(line[5..]);
					if (!Path.IsPathRooted(path))
					{
						// Relative to the directory that contains the trash ($topdir).
						var top = trashDir;
						var parent = Path.GetDirectoryName(trashDir);
						top = Path.GetFileName(trashDir).StartsWith(".Trash-", StringComparison.Ordinal) ? parent! : Path.GetDirectoryName(parent!)!;
						path = Path.Combine(top, path);
					}
				}
				else if (line.StartsWith("DeletionDate=", StringComparison.Ordinal))
				{
					DateTime.TryParse(line[13..], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date);
				}
			}
		}
		catch
		{
			// Unreadable info file.
		}
		return (path, date);
	}

	/// <summary>Restores an item to its original location. Returns the restored path.</summary>
	public string Restore(TrashEntry entry)
	{
		var target = entry.OriginalPath;
		var parent = Path.GetDirectoryName(target);
		if (parent is not null)
			Directory.CreateDirectory(parent);

		if (File.Exists(target) || Directory.Exists(target))
			target = FileOperationsService.GetUniqueName(target);

		if (Directory.Exists(entry.TrashedPath) && new DirectoryInfo(entry.TrashedPath).LinkTarget is null)
			FileOperationsService.MoveDirectory(entry.TrashedPath, target);
		else
			File.Move(entry.TrashedPath, target);

		TryDelete(entry.InfoPath);
		return target;
	}

	public void DeletePermanently(TrashEntry entry)
	{
		FileOperationsService.DeletePermanently(entry.TrashedPath);
		TryDelete(entry.InfoPath);
	}

	public void Empty()
	{
		foreach (var trash in GetTrashDirectories())
		{
			foreach (var sub in new[] { "files", "info" })
			{
				var dir = Path.Combine(trash, sub);
				if (!Directory.Exists(dir))
					continue;
				foreach (var entry in Directory.EnumerateFileSystemEntries(dir).ToList())
				{
					try
					{
						FileOperationsService.DeletePermanently(entry);
					}
					catch
					{
						// Keep going.
					}
				}
			}
			TryDelete(Path.Combine(trash, "directorysizes"));
		}
	}

	public bool IsEmpty() => !GetTrashDirectories().Any(t =>
	{
		try
		{
			return Directory.EnumerateFileSystemEntries(Path.Combine(t, "files")).Any();
		}
		catch
		{
			return false;
		}
	});

	private static void TryDelete(string file)
	{
		try
		{
			File.Delete(file);
		}
		catch
		{
			// Ignore.
		}
	}

	private static void TrySetMode(string path, UnixFileMode mode)
	{
		try
		{
			File.SetUnixFileMode(path, mode);
		}
		catch
		{
			// Ignore.
		}
	}

	/// <summary>Percent-encodes a path as required by the spec, keeping '/'.</summary>
	internal static string EncodePath(string path)
	{
		var sb = new StringBuilder();
		foreach (var b in Encoding.UTF8.GetBytes(path))
		{
			var c = (char)b;
			if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || "/-_.~!*'()".Contains(c))
				sb.Append(c);
			else
				sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
		}
		return sb.ToString();
	}
}
