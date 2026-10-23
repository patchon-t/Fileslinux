// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Files.App.Linux.Services;

/// <summary>
/// File clipboard. Keeps an in-app copy and mirrors it to the system clipboard using the
/// x-special/gnome-copied-files and text/uri-list formats, so copy/paste works with other file managers.
/// </summary>
public static class ClipboardService
{
	private const string GnomeFormat = "x-special/gnome-copied-files";
	private const string UriListFormat = "text/uri-list";

	public static IReadOnlyList<string> Paths { get; private set; } = [];

	public static bool IsCut { get; private set; }

	public static event EventHandler? Changed;

	public static IClipboard? SystemClipboard { get; set; }

	public static bool HasItems => Paths.Count > 0;

	public static async Task SetAsync(IReadOnlyList<string> paths, bool cut)
	{
		Paths = paths.ToList();
		IsCut = cut;
		Changed?.Invoke(null, EventArgs.Empty);

		if (SystemClipboard is null)
			return;
		try
		{
			var uris = paths.Select(p => new Uri(p).AbsoluteUri).ToList();
			var data = new DataObject();
			data.Set(GnomeFormat, Encoding.UTF8.GetBytes((cut ? "cut" : "copy") + "\n" + string.Join("\n", uris)));
			data.Set(UriListFormat, Encoding.UTF8.GetBytes(string.Join("\r\n", uris) + "\r\n"));
			data.Set(DataFormats.Text, string.Join("\n", paths));
			await SystemClipboard.SetDataObjectAsync(data);
		}
		catch
		{
			// The in-app clipboard still works.
		}
	}

	/// <summary>Reads files from the system clipboard if another app put them there; falls back to the in-app clipboard.</summary>
	public static async Task<(IReadOnlyList<string> Paths, bool Cut)> GetAsync()
	{
		if (SystemClipboard is not null)
		{
			try
			{
				var formats = await SystemClipboard.GetFormatsAsync();
				if (formats.Contains(GnomeFormat) && await SystemClipboard.GetDataAsync(GnomeFormat) is { } gnome)
				{
					var text = AsText(gnome);
					if (text is not null && ParseGnome(text) is { Paths.Count: > 0 } parsed)
						return SyncFromSystem(parsed.Paths, parsed.Cut);
				}
				if (formats.Contains(UriListFormat) && await SystemClipboard.GetDataAsync(UriListFormat) is { } uriList)
				{
					var text = AsText(uriList);
					var paths = ParseUriList(text ?? string.Empty);
					if (paths.Count > 0)
						return SyncFromSystem(paths, false);
				}
			}
			catch
			{
				// Use the in-app clipboard.
			}
		}
		return (Paths, IsCut);
	}

	private static (IReadOnlyList<string>, bool) SyncFromSystem(IReadOnlyList<string> paths, bool cut)
	{
		if (!paths.SequenceEqual(Paths) || cut != IsCut)
		{
			Paths = paths.ToList();
			IsCut = cut;
			Changed?.Invoke(null, EventArgs.Empty);
		}
		return (Paths, IsCut);
	}

	private static string? AsText(object data) => data switch
	{
		byte[] bytes => Encoding.UTF8.GetString(bytes),
		string s => s,
		_ => null,
	};

	internal static (List<string> Paths, bool Cut) ParseGnome(string text)
	{
		var lines = text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length == 0)
			return ([], false);
		bool cut = lines[0].Trim() == "cut";
		return (ParseUriList(string.Join("\n", lines.Skip(1))), cut);
	}

	internal static List<string> ParseUriList(string text)
	{
		var result = new List<string>();
		foreach (var line in text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			var l = line.Trim();
			if (l.StartsWith('#'))
				continue;
			if (Uri.TryCreate(l, UriKind.Absolute, out var uri) && uri.IsFile)
				result.Add(uri.LocalPath);
		}
		return result;
	}

	public static void Clear()
	{
		Paths = [];
		IsCut = false;
		Changed?.Invoke(null, EventArgs.Empty);
	}

	public static async Task SetTextAsync(string text)
	{
		if (SystemClipboard is not null)
			await SystemClipboard.SetTextAsync(text);
	}
}
