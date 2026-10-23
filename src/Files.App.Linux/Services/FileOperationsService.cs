// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Formats.Tar;
using System.IO.Compression;

namespace Files.App.Linux.Services;

public enum ConflictChoice
{
	Replace,
	KeepBoth,
	Skip,
	Cancel,
}

public sealed record ConflictResolution(ConflictChoice Choice, bool ApplyToAll);

/// <summary>Reports progress of a long-running operation.</summary>
public sealed class OperationProgress
{
	public long TotalBytes { get; set; }
	public long DoneBytes { get; set; }
	public int TotalItems { get; set; }
	public int DoneItems { get; set; }
	public string? CurrentItem { get; set; }
	public double Percent => TotalBytes > 0 ? Math.Min(100, DoneBytes * 100.0 / TotalBytes) : (TotalItems > 0 ? DoneItems * 100.0 / TotalItems : 0);
}

/// <summary>Copy, move, delete, rename, create, compress and extract.</summary>
public static class FileOperationsService
{
	/// <summary>Returns "name (2).ext", "name (3).ext", ... for a path that already exists.</summary>
	public static string GetUniqueName(string path)
	{
		if (!Exists(path))
			return path;

		var dir = Path.GetDirectoryName(path) ?? "/";
		var fileName = Path.GetFileName(path);
		bool isDir = Directory.Exists(path);
		string stem = fileName, ext = string.Empty;

		if (!isDir)
		{
			// Keep compound extensions like .tar.gz together.
			if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase))
			{
				var idx = fileName.LastIndexOf(".tar.", StringComparison.OrdinalIgnoreCase);
				stem = fileName[..idx];
				ext = fileName[idx..];
			}
			else if (Path.GetExtension(fileName) is { Length: > 0 } e && e.Length < fileName.Length)
			{
				stem = Path.GetFileNameWithoutExtension(fileName);
				ext = e;
			}
		}

		for (int i = 2; ; i++)
		{
			var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
			if (!Exists(candidate))
				return candidate;
		}
	}

	public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

	private static bool IsRealDirectory(string path) => Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null;

	public static string CreateFolder(string parent, string name)
	{
		ValidateName(name);
		var path = Path.Combine(parent, name);
		if (Exists(path))
			throw new IOException(Loc.Get("ItemAlreadyExistsDialogContent", "An item with this name already exists in this directory."));
		Directory.CreateDirectory(path);
		return path;
	}

	public static string CreateFile(string parent, string name)
	{
		ValidateName(name);
		var path = Path.Combine(parent, name);
		using (new FileStream(path, FileMode.CreateNew, FileAccess.Write))
		{
		}
		return path;
	}

	public static string Rename(string path, string newName)
	{
		ValidateName(newName);
		var parent = Path.GetDirectoryName(path.TrimEnd('/')) ?? "/";
		var target = Path.Combine(parent, newName);
		if (target == path)
			return path;

		// Allow case-only renames on case-insensitive file systems.
		if (Exists(target) && !string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
			throw new IOException(Loc.Get("ItemAlreadyExistsDialogContent", "An item with this name already exists in this directory."));

		if (IsRealDirectory(path))
			Directory.Move(path, target);
		else
			File.Move(path, target);
		return target;
	}

	public static void ValidateName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException(Loc.Get("ErrorInputEmpty", "Input field cannot be empty!"));
		if (name.Contains('/') || name.Contains('\0') || name is "." or "..")
			throw new ArgumentException("The name can't contain \"/\" or be \".\" or \"..\".");
	}

	public static void DeletePermanently(string path)
	{
		var info = new FileInfo(path);
		if (info.LinkTarget is not null || File.Exists(path))
		{
			// Symlinks are removed themselves, never their targets.
			File.Delete(path);
			return;
		}
		if (Directory.Exists(path))
		{
			try
			{
				Directory.Delete(path, true);
			}
			catch (UnauthorizedAccessException)
			{
				MakeWritable(path);
				Directory.Delete(path, true);
			}
		}
	}

	private static void MakeWritable(string dir)
	{
		foreach (var d in Directory.EnumerateDirectories(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }).Prepend(dir))
		{
			try
			{
				var mode = File.GetUnixFileMode(d);
				File.SetUnixFileMode(d, mode | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.UserRead);
			}
			catch
			{
				// Ignore.
			}
		}
	}

	/// <summary>Total size of files and number of items for progress reporting.</summary>
	public static (long Bytes, int Files, int Folders) Measure(IEnumerable<string> paths, CancellationToken ct = default)
	{
		long bytes = 0;
		int files = 0, folders = 0;
		foreach (var p in paths)
		{
			ct.ThrowIfCancellationRequested();
			if (IsRealDirectory(p))
			{
				folders++;
				foreach (var entry in new DirectoryInfo(p).EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
				{
					ct.ThrowIfCancellationRequested();
					if (entry is FileInfo f)
					{
						files++;
						if (f.LinkTarget is null)
							bytes += SafeLength(f);
					}
					else
					{
						folders++;
					}
				}
			}
			else if (Exists(p))
			{
				files++;
				var fi = new FileInfo(p);
				if (fi.LinkTarget is null)
					bytes += SafeLength(fi);
			}
		}
		return (bytes, files, folders);
	}

	private static long SafeLength(FileInfo f)
	{
		try
		{
			return f.Length;
		}
		catch
		{
			return 0;
		}
	}

	/// <summary>Copies or moves items into <paramref name="destinationFolder"/>. Returns the paths that were created.</summary>
	public static async Task<List<string>> TransferAsync(
		IReadOnlyList<string> sources,
		string destinationFolder,
		bool move,
		Func<string, Task<ConflictResolution>> onConflict,
		IProgress<OperationProgress>? progress = null,
		CancellationToken ct = default)
	{
		var created = new List<string>();
		var state = new OperationProgress();
		ConflictResolution? applyAll = null;

		await Task.Run(() =>
		{
			var (bytes, files, _) = Measure(sources, ct);
			state.TotalBytes = bytes;
			state.TotalItems = Math.Max(files, sources.Count);
		}, ct).ConfigureAwait(false);
		progress?.Report(state);

		foreach (var source in sources)
		{
			ct.ThrowIfCancellationRequested();
			var name = Path.GetFileName(source.TrimEnd('/'));
			var target = Path.Combine(destinationFolder, name);
			var normalizedSource = Path.GetFullPath(source).TrimEnd('/');
			var normalizedDest = Path.GetFullPath(destinationFolder).TrimEnd('/');

			if (IsRealDirectory(source) && (normalizedDest == normalizedSource || normalizedDest.StartsWith(normalizedSource + "/", StringComparison.Ordinal)))
				throw new IOException($"Can't {(move ? "move" : "copy")} \"{name}\" into itself.");

			bool sameLocation = Path.GetDirectoryName(normalizedSource) == normalizedDest;
			if (sameLocation && move)
				continue;

			if (sameLocation)
			{
				// Copying into the same folder always creates a copy.
				target = GetUniqueName(target);
			}
			else if (Exists(target))
			{
				var resolution = applyAll ?? await onConflict(target).ConfigureAwait(false);
				if (resolution.ApplyToAll)
					applyAll = resolution;

				switch (resolution.Choice)
				{
					case ConflictChoice.Cancel:
						return created;
					case ConflictChoice.Skip:
						continue;
					case ConflictChoice.KeepBoth:
						target = GetUniqueName(target);
						break;
					case ConflictChoice.Replace:
						if (IsRealDirectory(target) && IsRealDirectory(source))
						{
							// Merge folders.
							break;
						}
						await Task.Run(() => DeletePermanently(target), ct).ConfigureAwait(false);
						break;
				}
			}

			state.CurrentItem = name;
			progress?.Report(state);

			await Task.Run(() =>
			{
				if (move)
					MoveItem(source, target, state, progress, ct);
				else
					CopyItem(source, target, state, progress, ct);
			}, ct).ConfigureAwait(false);

			created.Add(target);
		}

		return created;
	}

	private static void MoveItem(string source, string target, OperationProgress state, IProgress<OperationProgress>? progress, CancellationToken ct)
	{
		try
		{
			if (IsRealDirectory(source))
			{
				if (Directory.Exists(target))
				{
					// Merge into existing folder.
					CopyItem(source, target, state, progress, ct);
					DeletePermanently(source);
				}
				else
				{
					Directory.Move(source, target);
				}
			}
			else
			{
				File.Move(source, target, true);
			}
			state.DoneItems++;
			progress?.Report(state);
		}
		catch (IOException) when (IsRealDirectory(source))
		{
			// Different file system: copy then delete.
			CopyItem(source, target, state, progress, ct);
			DeletePermanently(source);
		}
	}

	internal static void MoveDirectory(string source, string target)
	{
		try
		{
			Directory.Move(source, target);
		}
		catch (IOException)
		{
			CopyItem(source, target, new OperationProgress(), null, CancellationToken.None);
			DeletePermanently(source);
		}
	}

	private static void CopyItem(string source, string target, OperationProgress state, IProgress<OperationProgress>? progress, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var info = new FileInfo(source);

		if (info.LinkTarget is { } link)
		{
			if (Exists(target))
				DeletePermanently(target);
			File.CreateSymbolicLink(target, link);
			state.DoneItems++;
			return;
		}

		if (Directory.Exists(source))
		{
			Directory.CreateDirectory(target);
			foreach (var entry in Directory.EnumerateFileSystemEntries(source))
				CopyItem(entry, Path.Combine(target, Path.GetFileName(entry)), state, progress, ct);
			TryCopyMetadata(source, target, true);
			return;
		}

		state.CurrentItem = info.Name;
		using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
		using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
		{
			var buffer = new byte[1 << 20];
			int read;
			var lastReport = Environment.TickCount64;
			while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
			{
				ct.ThrowIfCancellationRequested();
				output.Write(buffer, 0, read);
				state.DoneBytes += read;
				if (Environment.TickCount64 - lastReport > 100)
				{
					progress?.Report(state);
					lastReport = Environment.TickCount64;
				}
			}
		}
		TryCopyMetadata(source, target, false);
		state.DoneItems++;
		progress?.Report(state);
	}

	private static void TryCopyMetadata(string source, string target, bool isDir)
	{
		try
		{
			File.SetUnixFileMode(target, File.GetUnixFileMode(source));
			if (isDir)
				Directory.SetLastWriteTime(target, Directory.GetLastWriteTime(source));
			else
				File.SetLastWriteTime(target, File.GetLastWriteTime(source));
		}
		catch
		{
			// Metadata is best-effort.
		}
	}

	/// <summary>Creates a .zip archive from the given items.</summary>
	public static async Task<string> CompressToZipAsync(IReadOnlyList<string> sources, string archivePath, CancellationToken ct = default)
	{
		archivePath = GetUniqueName(archivePath);
		await Task.Run(() =>
		{
			using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
			foreach (var source in sources)
			{
				ct.ThrowIfCancellationRequested();
				var name = Path.GetFileName(source.TrimEnd('/'));
				if (IsRealDirectory(source))
				{
					zip.CreateEntry(name + "/");
					foreach (var file in Directory.EnumerateFileSystemEntries(source, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
					{
						ct.ThrowIfCancellationRequested();
						var rel = name + "/" + Path.GetRelativePath(source, file);
						if (IsRealDirectory(file))
							zip.CreateEntry(rel + "/");
						else if (File.Exists(file))
							zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
					}
				}
				else
				{
					zip.CreateEntryFromFile(source, name, CompressionLevel.Optimal);
				}
			}
		}, ct).ConfigureAwait(false);
		return archivePath;
	}

	/// <summary>Extracts a .zip, .tar, .tar.gz or .tgz archive into a new folder next to it.</summary>
	public static async Task<string> ExtractAsync(string archivePath, string? destinationFolder = null, CancellationToken ct = default)
	{
		var parent = Path.GetDirectoryName(archivePath) ?? "/";
		var name = Path.GetFileName(archivePath);
		var lower = name.ToLowerInvariant();
		var stem = lower.EndsWith(".tar.gz") ? name[..^7] : Path.GetFileNameWithoutExtension(name);
		var dest = destinationFolder ?? GetUniqueName(Path.Combine(parent, stem));
		Directory.CreateDirectory(dest);

		await Task.Run(() =>
		{
			if (lower.EndsWith(".zip"))
			{
				ZipFile.ExtractToDirectory(archivePath, dest, overwriteFiles: false);
			}
			else
			{
				using var fs = File.OpenRead(archivePath);
				Stream stream = lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz") ? new GZipStream(fs, CompressionMode.Decompress) : fs;
				TarFile.ExtractToDirectory(stream, dest, overwriteFiles: false);
				if (!ReferenceEquals(stream, fs))
					stream.Dispose();
			}
		}, ct).ConfigureAwait(false);
		return dest;
	}
}
