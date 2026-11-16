// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.App.Linux.Models;

namespace Files.App.Linux.Services;

public enum GroupColumn
{
	None,
	Name,
	DateModified,
	DateCreated,
	Type,
	Size,
}

/// <summary>A collapsible group header shown above its items ("Last week", "Earlier this year"...).</summary>
public sealed partial class GroupHeader : ObservableObject
{
	public required string Key { get; init; }

	public required string Title { get; init; }

	/// <summary>Order of the group (lower first).</summary>
	public int Order { get; init; }

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial bool IsCollapsed { get; set; }

	public string CountText => $"({Count})";

	partial void OnCountChanged(int value) => OnPropertyChanged(nameof(CountText));
}

/// <summary>
/// Groups items like Windows Explorer and Files: by date (Today, Yesterday, Earlier this week, Last week,
/// Earlier this month, Last month, Earlier this year, Last year, A long time ago), type, first letter or size.
/// </summary>
public static class GroupingService
{
	/// <summary>Group key, title and order for an item. <paramref name="now"/> is passed in so tests can fix the date.</summary>
	public static (string Key, string Title, int Order) GroupOf(FileItem item, GroupColumn column, DateTime now)
	{
		switch (column)
		{
			case GroupColumn.DateModified:
				return DateGroup(item.TrashEntry?.DeletionDate ?? item.Modified, now);
			case GroupColumn.DateCreated:
				return DateGroup(item.Created, now);
			case GroupColumn.Type:
				// Folders first, then types alphabetically.
				return item.IsDirectory ? ("type:folder", item.TypeDescription, -1) : ("type:" + item.TypeDescription, item.TypeDescription, 0);
			case GroupColumn.Name:
				var first = item.Name.TrimStart('.').FirstOrDefault();
				if (char.IsLetter(first))
				{
					var letter = char.ToUpper(first, CultureInfo.CurrentCulture).ToString();
					return ("name:" + letter, letter, 1);
				}
				return char.IsDigit(first) ? ("name:0-9", "0–9", 0) : ("name:#", "#", 2);
			case GroupColumn.Size:
				return SizeGroup(item);
			default:
				return (string.Empty, string.Empty, 0);
		}
	}

	private static (string, string, int) DateGroup(DateTime date, DateTime now)
	{
		var today = now.Date;
		var d = date.Date;
		var weekStart = today.AddDays(-(((int)today.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7));
		var monthStart = new DateTime(today.Year, today.Month, 1);
		var yearStart = new DateTime(today.Year, 1, 1);

		if (date == DateTime.MinValue)
			return ("date:unknown", Loc.Get("Unknown", "Unknown"), 99);
		if (d > today)
			return ("date:future", Loc.Get("InTheFuture", "In the future"), -1);
		if (d == today)
			return ("date:today", Loc.Get("Today", "Today"), 0);
		if (d == today.AddDays(-1))
			return ("date:yesterday", Loc.Get("Yesterday", "Yesterday"), 1);
		if (d >= weekStart)
			return ("date:thisweek", Loc.Get("EarlierThisWeek", "Earlier this week"), 2);
		if (d >= weekStart.AddDays(-7))
			return ("date:lastweek", Loc.Get("LastWeek", "Last week"), 3);
		if (d >= monthStart)
			return ("date:thismonth", Loc.Get("EarlierThisMonth", "Earlier this month"), 4);
		if (d >= monthStart.AddMonths(-1))
			return ("date:lastmonth", Loc.Get("LastMonth", "Last month"), 5);
		if (d >= yearStart)
			return ("date:thisyear", Loc.Get("EarlierThisYear", "Earlier this year"), 6);
		if (d >= yearStart.AddYears(-1))
			return ("date:lastyear", Loc.Get("LastYear", "Last year"), 7);
		return ("date:older", Loc.Get("Older", "A long time ago"), 8);
	}

	/// <summary>Windows Explorer's size groups.</summary>
	private static (string, string, int) SizeGroup(FileItem item)
	{
		if (item.IsDirectory)
			return ("size:folder", Loc.Get("Folders", "Folders"), 0);
		const long KB = 1024, MB = 1024 * KB, GB = 1024 * MB;
		var s = item.Size;
		return s switch
		{
			0 => ("size:empty", "Empty (0 KB)", 1),
			< 16 * KB => ("size:tiny", "Tiny (0 – 16 KB)", 2),
			< 1 * MB => ("size:small", "Small (16 KB – 1 MB)", 3),
			< 128 * MB => ("size:medium", "Medium (1 – 128 MB)", 4),
			< 1 * GB => ("size:large", "Large (128 MB – 1 GB)", 5),
			< 4 * GB => ("size:huge", "Huge (1 – 4 GB)", 6),
			_ => ("size:gigantic", "Gigantic (> 4 GB)", 7),
		};
	}

	/// <summary>
	/// Builds the list shown in a layout: each group header followed by its items (none for collapsed groups).
	/// Items keep the order they come in (already sorted). Date groups go newest first; others go A–Z / small to large.
	/// </summary>
	public static List<object> Build(IReadOnlyList<FileItem> items, GroupColumn column, ISet<string> collapsed, DateTime now, Dictionary<string, GroupHeader>? reuse = null)
	{
		if (column == GroupColumn.None)
			return [.. items];

		var groups = new Dictionary<string, (GroupHeader Header, List<FileItem> Items)>(StringComparer.Ordinal);
		foreach (var item in items)
		{
			var (key, title, order) = GroupOf(item, column, now);
			if (!groups.TryGetValue(key, out var group))
			{
				var header = reuse is not null && reuse.TryGetValue(key, out var existing) ? existing : new GroupHeader { Key = key, Title = title, Order = order };
				group = (header, []);
				groups[key] = group;
			}
			group.Items.Add(item);
		}

		var result = new List<object>(items.Count + groups.Count);
		foreach (var (header, groupItems) in groups.Values.OrderBy(g => g.Header.Order).ThenBy(g => g.Header.Title, StringComparer.CurrentCultureIgnoreCase))
		{
			header.Count = groupItems.Count;
			header.IsCollapsed = collapsed.Contains(header.Key);
			result.Add(header);
			if (!header.IsCollapsed)
				result.AddRange(groupItems);
		}
		return result;
	}
}
