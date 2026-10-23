// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;

namespace Files.App.Linux.Helpers;

public static class ResourceHelper
{
	/// <summary>Binds a property to a theme resource so it follows light/dark switches (like {DynamicResource} in XAML).</summary>
	public static T WithResource<T>(this T control, AvaloniaProperty property, string key)
		where T : Control
	{
		control.Bind(property, control.GetResourceObservable(key));
		return control;
	}
}
