// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Files.App.Linux.Models;

namespace Files.App.Linux.Services;

/// <summary>Lists folders, searches and sorts items.</summary>
public static class FileSystemService
{
	public static List<FileItem> Enumerate(string path, bool showHidden, CancellationToken ct = default)
	{
		var items = new List<FileItem>();
		var dir = new DirectoryInfo(path);
		var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, ReturnSpecialDirectories = false };

		foreach (var info in dir.EnumerateFileSystemInfos("*", options))
		{
			ct.ThrowIfCancellationRequested();
			if (!showHidden && info.Name.StartsWith('.'))
				continue;

			FileSystemInfo resolved = info;
			if (info.LinkTarget is not null && info is FileInfo)
			{
				// Show symlinks to folders as folders.
				try
				{
					if (Directory.Exists(info.FullName))
						resolved = new DirectoryInfo(info.FullName);
				}
				catch
				{
					// Broken link.
				}
			}
			items.Add(FileItem.FromInfo(resolved));
		}
		return items;
	}

	/// <summary>Recursively searches below <paramref name="root"/>. Supports * and ? wildcards; otherwise matches substrings.</summary>
	public static async IAsyncEnumerable<FileItem> SearchAsync(string root, string query, bool showHidden, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var channel = System.Threading.Channels.Channel.CreateUnbounded<FileItem>();
		_ = Task.Run(() =>
		{
			try
			{
				var match = BuildMatcher(query);
				var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = 0 };
				var stack = new Stack<string>();
				stack.Push(root);
				while (stack.Count > 0)
				{
					ct.ThrowIfCancellationRequested();
					var current = stack.Pop();
					IEnumerable<FileSystemInfo> entries;
					try
					{
						entries = new DirectoryInfo(current).EnumerateFileSystemInfos("*", options);
					}
					catch
					{
						continue;
					}

					foreach (var e in entries)
					{
						ct.ThrowIfCancellationRequested();
						if (!showHidden && e.Name.StartsWith('.'))
							continue;
						if (match(e.Name))
							channel.Writer.TryWrite(FileItem.FromInfo(e));
						if (e is DirectoryInfo d && d.LinkTarget is null)
							stack.Push(d.FullName);
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				channel.Writer.TryComplete();
			}
		}, ct);

		await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
			yield return item;
	}

	internal static Func<string, bool> BuildMatcher(string query)
	{
		query = query.Trim();
		if (query.Contains('*') || query.Contains('?'))
		{
			var regex = "^" + System.Text.RegularExpressions.Regex.Escape(query).Replace("\\*", ".*").Replace("\\?", ".") + "$";
			var r = new System.Text.RegularExpressions.Regex(regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
			return r.IsMatch;
		}
		return name => name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
	}

	public static IComparer<FileItem> GetComparer(SortColumn column, bool descending, bool foldersFirst)
		=> new FileItemComparer(column, descending, foldersFirst);

	private sealed class FileItemComparer(SortColumn column, bool descending, bool foldersFirst) : IComparer<FileItem>
	{
		private static readonly StringComparer NameComparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.NumericOrdering);

		public int Compare(FileItem? x, FileItem? y)
		{
			if (x is null || y is null)
				return 0;
			if (foldersFirst && x.IsDirectory != y.IsDirectory)
				return x.IsDirectory ? -1 : 1;

			int result = column switch
			{
				SortColumn.DateModified => x.Modified.CompareTo(y.Modified),
				SortColumn.DateCreated => x.Created.CompareTo(y.Created),
				SortColumn.Type => string.Compare(x.TypeDescription, y.TypeDescription, StringComparison.CurrentCultureIgnoreCase),
				SortColumn.Size => x.Size.CompareTo(y.Size),
				_ => 0,
			};
			if (result == 0)
				result = NameComparer.Compare(x.Name, y.Name);
			return descending ? -result : result;
		}
	}

	/// <summary>Recently used files from ~/.local/share/recently-used.xbel (shared with GTK apps).</summary>
	public static List<FileItem> GetRecentFiles(int max = 20)
	{
		var result = new List<FileItem>();
		try
		{
			var file = Path.Combine(XdgPaths.DataHome, "recently-used.xbel");
			if (!File.Exists(file))
				return result;

			var doc = XDocument.Load(file);
			var entries = doc.Root?.Elements("bookmark")
				.Select(b => (Href: b.Attribute("href")?.Value, Modified: b.Attribute("modified")?.Value ?? b.Attribute("visited")?.Value))
				.Where(b => b.Href?.StartsWith("file://", StringComparison.Ordinal) == true)
				.OrderByDescending(b => b.Modified, StringComparer.Ordinal) ?? Enumerable.Empty<(string?, string?)>();

			foreach (var (href, _) in entries)
			{
				var path = new Uri(href!).LocalPath;
				if (!File.Exists(path))
					continue;
				result.Add(FileItem.FromPath(path));
				if (result.Count >= max)
					break;
			}
		}
		catch
		{
			// Ignore malformed history.
		}
		return result;
	}

	/// <summary>Recursively sizes a folder.</summary>
	public static async Task<(long Bytes, int Files, int Folders)> MeasureAsync(string path, CancellationToken ct = default)
		=> await Task.Run(() => FileOperationsService.Measure([path], ct), ct).ConfigureAwait(false);
}
