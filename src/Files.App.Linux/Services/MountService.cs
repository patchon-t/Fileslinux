// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text;

namespace Files.App.Linux.Services;

public sealed record MountEntry(string MountPoint, string FsType, string Source, string Options)
{
	public bool IsReadOnly => Options.Split(',').Contains("ro");
}

public sealed record DriveEntry(string Name, string MountPoint, string Device, string FsType, long TotalBytes, long FreeBytes, bool IsRemovable, bool IsRoot)
{
	public long UsedBytes => TotalBytes - FreeBytes;
	public double UsedPercent => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes * 100.0;
	public string SpaceText => TotalBytes <= 0 ? string.Empty : $"{FormatHelpers.FormatSize(FreeBytes)} free of {FormatHelpers.FormatSize(TotalBytes)}";
	public bool IsLowOnSpace => UsedPercent >= 90;
}

/// <summary>Reads mount points from /proc/self/mountinfo.</summary>
public static class MountService
{
	private static readonly HashSet<string> PhysicalFsTypes = new(StringComparer.Ordinal)
	{
		"ext2", "ext3", "ext4", "btrfs", "xfs", "zfs", "f2fs", "jfs", "reiserfs", "vfat", "fat", "msdos", "exfat", "ntfs", "ntfs3", "fuseblk",
		"hfs", "hfsplus", "apfs", "iso9660", "udf", "nfs", "nfs4", "cifs", "smb3", "sshfs", "fuse.sshfs", "9p", "virtiofs", "bcachefs", "overlay",
	};

	public static IReadOnlyList<MountEntry> GetMounts()
	{
		try
		{
			return ParseMountInfo(File.ReadAllLines("/proc/self/mountinfo"));
		}
		catch
		{
			return [];
		}
	}

	internal static List<MountEntry> ParseMountInfo(IEnumerable<string> lines)
	{
		var list = new List<MountEntry>();
		foreach (var line in lines)
		{
			var sep = line.IndexOf(" - ", StringComparison.Ordinal);
			if (sep < 0)
				continue;

			var left = line[..sep].Split(' ');
			var right = line[(sep + 3)..].Split(' ');
			if (left.Length < 6 || right.Length < 2)
				continue;

			list.Add(new MountEntry(Unescape(left[4]), right[0], Unescape(right[1]), left[5]));
		}
		return list;
	}

	/// <summary>Decodes octal escapes such as \040 (space) used by the kernel.</summary>
	internal static string Unescape(string s)
	{
		if (!s.Contains('\\'))
			return s;

		var bytes = new List<byte>();
		for (int i = 0; i < s.Length; i++)
		{
			if (s[i] == '\\' && IsOctal(s, i + 1))
			{
				bytes.Add(Convert.ToByte(s.Substring(i + 1, 3), 8));
				i += 3;
			}
			else
			{
				bytes.AddRange(Encoding.UTF8.GetBytes(s[i].ToString()));
			}
		}
		return Encoding.UTF8.GetString(bytes.ToArray());
	}

	private static bool IsOctal(string s, int start)
	{
		if (start + 3 > s.Length)
			return false;
		for (int i = start; i < start + 3; i++)
		{
			if (s[i] < '0' || s[i] > '7')
				return false;
		}
		return true;
	}

	/// <summary>Finds the mount point that contains <paramref name="path"/>.</summary>
	public static string GetMountPoint(string path, IReadOnlyList<MountEntry>? mounts = null)
	{
		mounts ??= GetMounts();
		var full = Path.GetFullPath(path);
		string best = "/";
		foreach (var m in mounts)
		{
			var mp = m.MountPoint;
			if (mp.Length > best.Length && (full == mp || full.StartsWith(mp.TrimEnd('/') + "/", StringComparison.Ordinal)))
				best = mp;
		}
		return best;
	}

	/// <summary>Drives shown in the sidebar and on the Home page.</summary>
	public static IReadOnlyList<DriveEntry> GetDrives()
	{
		var result = new List<DriveEntry>();
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var m in GetMounts())
		{
			if (!PhysicalFsTypes.Contains(m.FsType) && !m.FsType.StartsWith("fuse.", StringComparison.Ordinal))
				continue;

			var mp = m.MountPoint;
			bool isRoot = mp == "/";
			bool isUserMount = mp.StartsWith("/media/", StringComparison.Ordinal) || mp.StartsWith("/run/media/", StringComparison.Ordinal) || mp.StartsWith("/mnt/", StringComparison.Ordinal) || mp == "/mnt" || mp.StartsWith(XdgPaths.Home + "/", StringComparison.Ordinal);
			bool isGvfs = mp.Contains("/gvfs", StringComparison.Ordinal) || m.FsType == "fuse.portal" || m.FsType == "fuse.gvfsd-fuse";
			if ((!isRoot && !isUserMount) || isGvfs)
				continue;
			if (m.FsType == "overlay" && !isRoot)
				continue;
			if (!seen.Add(mp))
				continue;

			long total = 0, free = 0;
			try
			{
				var di = new DriveInfo(mp);
				total = di.TotalSize;
				free = di.AvailableFreeSpace;
			}
			catch
			{
				// Unavailable drive (e.g. disconnected network share).
			}

			var name = isRoot ? Loc.Get("FileSystem", "File System") : Path.GetFileName(mp.TrimEnd('/'));
			result.Add(new DriveEntry(name, mp, m.Source, m.FsType, total, free, !isRoot && !mp.StartsWith("/mnt", StringComparison.Ordinal), isRoot));
		}

		return result.OrderByDescending(d => d.IsRoot).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}
}
