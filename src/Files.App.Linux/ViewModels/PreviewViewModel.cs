// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.App.Linux.Models;
using Files.App.Linux.Services;

namespace Files.App.Linux.ViewModels;

public sealed record PropertyRow(string Name, string Value);

/// <summary>Details / preview pane content for the selected item(s).</summary>
public sealed partial class PreviewViewModel : ObservableObject
{
	private CancellationTokenSource? _cts;

	[ObservableProperty]
	public partial FileItem? Item { get; set; }

	[ObservableProperty]
	public partial string Title { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Subtitle { get; set; } = string.Empty;

	[ObservableProperty]
	public partial Bitmap? Image { get; set; }

	[ObservableProperty]
	public partial string? Text { get; set; }

	[ObservableProperty]
	public partial bool HasSelection { get; set; }

	[ObservableProperty]
	public partial bool IsMultiple { get; set; }

	[ObservableProperty]
	public partial IReadOnlyList<PropertyRow> Properties { get; set; } = [];

	public bool HasImage => Image is not null;

	public bool HasText => !string.IsNullOrEmpty(Text);

	partial void OnImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));

	partial void OnTextChanged(string? value) => OnPropertyChanged(nameof(HasText));

	public void Update(IReadOnlyList<FileItem> selection)
	{
		_cts?.Cancel();
		_cts = new CancellationTokenSource();
		var ct = _cts.Token;

		Image = null;
		Text = null;
		Properties = [];
		HasSelection = selection.Count > 0;
		IsMultiple = selection.Count > 1;

		if (selection.Count == 0)
		{
			Item = null;
			Title = Loc.Get("NoItemSelected", "No item selected");
			Subtitle = string.Empty;
			return;
		}

		if (selection.Count > 1)
		{
			Item = null;
			Title = Loc.Format("SelectedItems", "{0, plural, one {# item selected} other {# items selected}}", selection.Count);
			var size = selection.Where(s => !s.IsDirectory).Sum(s => s.Size);
			Subtitle = FormatHelpers.FormatSize(size);
			return;
		}

		var item = selection[0];
		Item = item;
		Title = item.Name;
		Subtitle = item.TypeDescription;

		var rows = new List<PropertyRow>
		{
			new(Loc.Get("PropertyItemTypeText", "Item type"), item.TypeDescription),
		};
		if (!item.IsDirectory)
			rows.Add(new(Loc.Get("Size", "Size"), FormatHelpers.FormatSize(item.Size)));
		rows.Add(new(Loc.Get("DateModifiedLowerCase", "Date modified"), item.ModifiedText));
		rows.Add(new(Loc.Get("DateCreated", "Date created"), item.CreatedText));
		if (item.TrashEntry is { } t)
		{
			rows.Add(new(Loc.Get("OriginalPath", "Original path"), t.OriginalPath));
			rows.Add(new(Loc.Get("DateDeleted", "Date deleted"), item.DeletedText));
		}
		else
		{
			rows.Add(new(Loc.Get("Path", "Path"), item.FullPath));
		}
		Properties = rows;

		_ = LoadContentAsync(item, rows, ct);
	}

	private async Task LoadContentAsync(FileItem item, List<PropertyRow> rows, CancellationToken ct)
	{
		try
		{
			if (item.IsDirectory)
			{
				var count = await Task.Run(() =>
				{
					try
					{
						return Directory.EnumerateFileSystemEntries(item.FullPath).Take(100000).Count();
					}
					catch
					{
						return -1;
					}
				}, ct);
				if (ct.IsCancellationRequested || count < 0)
					return;
				var list = new List<PropertyRow>(rows) { new(Loc.Get("Items", "Items"), Loc.Format("GroupItemsCount_Plural", "{0} items", count)) };
				Properties = list;
				return;
			}

			if (ThumbnailService.CanThumbnail(item))
			{
				var bitmap = await Task.Run(() => ThumbnailService.Load(item.FullPath, 560), ct);
				if (!ct.IsCancellationRequested && bitmap is not null)
				{
					Image = bitmap;
					var list = new List<PropertyRow>(rows);
					list.Insert(2, new(Loc.Get("Dimensions", "Dimensions"), $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}"));
					Properties = list;
				}
				return;
			}

			if (FileTypes.IsTextPreviewable(item.Extension) && item.Size < 4 * 1024 * 1024)
			{
				var text = await Task.Run(() => ReadTextPreview(item.FullPath), ct);
				if (!ct.IsCancellationRequested)
					Text = text;
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
			// Preview is best-effort.
		}
	}

	internal static string? ReadTextPreview(string path, int maxBytes = 32 * 1024)
	{
		using var fs = File.OpenRead(path);
		var buffer = new byte[Math.Min(maxBytes, (int)Math.Min(int.MaxValue, fs.Length))];
		var read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
		if (buffer.AsSpan(0, read).IndexOf((byte)0) >= 0)
			return null; // Binary.
		return Encoding.UTF8.GetString(buffer, 0, read);
	}
}
