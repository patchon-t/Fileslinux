// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;

namespace Files.App.Linux.Services;

public static class FormatHelpers
{
	private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

	public static string FormatSize(long bytes)
	{
		if (bytes < 1024)
			return bytes.ToString(CultureInfo.CurrentCulture) + " B";

		double size = bytes;
		int unit = 0;
		while (size >= 1024 && unit < Units.Length - 1)
		{
			size /= 1024;
			unit++;
		}
		return size.ToString(size < 10 ? "0.##" : size < 100 ? "0.#" : "0", CultureInfo.CurrentCulture) + " " + Units[unit];
	}

	public static string FormatSizeWithBytes(long bytes)
		=> $"{FormatSize(bytes)} ({bytes.ToString("N0", CultureInfo.CurrentCulture)} {Loc.Get("Bytes", "bytes").ToLower(CultureInfo.CurrentCulture)})";

	public static string FormatDate(DateTime date)
	{
		if (date == DateTime.MinValue || date.Year < 1971)
			return string.Empty;
		return date.ToString("g", CultureInfo.CurrentCulture);
	}
}
