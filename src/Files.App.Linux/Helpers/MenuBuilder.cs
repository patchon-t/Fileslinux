// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Files.App.Linux.Helpers;

/// <summary>Small helpers to build menus with Fluent icons in code.</summary>
public static class MenuBuilder
{
	public static MenuItem Item(string header, Symbol? icon, ICommand? command, object? parameter = null, string? gesture = null, bool isEnabled = true, IBrush? iconBrush = null)
	{
		var item = new MenuItem
		{
			Header = header,
			Command = command,
			CommandParameter = parameter,
			IsEnabled = isEnabled,
		};
		if (icon is { } s)
			item.Icon = new SymbolIcon { Symbol = s, FontSize = 16, Foreground = iconBrush ?? item.Foreground, IconVariant = iconBrush is null ? IconVariant.Regular : IconVariant.Filled };
		if (gesture is not null)
			item.InputGesture = KeyGesture.Parse(gesture);
		return item;
	}

	public static MenuItem Action(string header, Symbol? icon, Action action, string? gesture = null, bool isEnabled = true)
	{
		var item = Item(header, icon, null, null, gesture, isEnabled);
		item.Click += (_, _) => action();
		return item;
	}

	public static MenuItem Check(string header, bool isChecked, Action action, string? gesture = null)
	{
		var item = new MenuItem
		{
			Header = header,
			ToggleType = MenuItemToggleType.CheckBox,
			IsChecked = isChecked,
		};
		if (gesture is not null)
			item.InputGesture = KeyGesture.Parse(gesture);
		item.Click += (_, _) => action();
		return item;
	}

	public static MenuItem Radio(string header, bool isChecked, Action action, Symbol? icon = null, string? gesture = null)
	{
		var item = new MenuItem
		{
			Header = header,
			ToggleType = MenuItemToggleType.Radio,
			IsChecked = isChecked,
		};
		if (icon is { } s)
			item.Icon = new SymbolIcon { Symbol = s, FontSize = 16 };
		if (gesture is not null)
			item.InputGesture = KeyGesture.Parse(gesture);
		item.Click += (_, _) => action();
		return item;
	}

	public static MenuItem Sub(string header, Symbol? icon, params object[] children)
	{
		var item = new MenuItem { Header = header };
		if (icon is { } s)
			item.Icon = new SymbolIcon { Symbol = s, FontSize = 16 };
		foreach (var c in children)
			item.Items.Add(c);
		return item;
	}

	public static Separator Separator() => new();

	/// <summary>Removes leading/trailing/double separators.</summary>
	public static void Tidy(System.Collections.IList items)
	{
		for (int i = items.Count - 1; i >= 0; i--)
		{
			bool isSep = items[i] is Separator;
			bool prevSep = i == 0 || items[i - 1] is Separator;
			bool last = i == items.Count - 1;
			if (isSep && (prevSep || last))
				items.RemoveAt(i);
		}
		while (items.Count > 0 && items[0] is Separator)
			items.RemoveAt(0);
	}
}
