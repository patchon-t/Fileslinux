// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Files.App.Linux.Models;

namespace Files.App.Linux.Services;

/// <summary>Loads image thumbnails in the background.</summary>
public static class ThumbnailService
{
	private static readonly SemaphoreSlim Gate = new(Math.Max(2, Environment.ProcessorCount / 2));
	private const long MaxFileSize = 64L * 1024 * 1024;
	private static readonly HashSet<string> Decodable = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico" };

	public static bool CanThumbnail(FileItem item) => !item.IsDirectory && Decodable.Contains(item.Extension) && item.Size is > 0 and < MaxFileSize;

	public static void QueueThumbnails(IEnumerable<FileItem> items, int width, CancellationToken ct)
	{
		if (!SettingsService.Current.ShowThumbnails)
			return;

		foreach (var item in items.Where(CanThumbnail).ToList())
		{
			_ = Task.Run(async () =>
			{
				await Gate.WaitAsync(ct).ConfigureAwait(false);
				try
				{
					ct.ThrowIfCancellationRequested();
					var bitmap = Load(item.FullPath, width);
					if (bitmap is not null)
						await Dispatcher.UIThread.InvokeAsync(() => item.Thumbnail = bitmap);
				}
				catch (OperationCanceledException)
				{
				}
				finally
				{
					Gate.Release();
				}
			}, ct);
		}
	}

	public static Bitmap? Load(string path, int width)
	{
		try
		{
			using var stream = File.OpenRead(path);
			return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
		}
		catch
		{
			return null;
		}
	}
}
