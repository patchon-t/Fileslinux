// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia.Markup.Xaml;
using Files.App.Linux.Services;

namespace Files.App.Linux.Views;

/// <summary>XAML markup extension for localized strings: {v:Loc Key, English fallback}.</summary>
public sealed class LocExtension : MarkupExtension
{
	public LocExtension(string key)
	{
		Key = key;
	}

	public LocExtension(string key, string fallback)
	{
		Key = key;
		Fallback = fallback;
	}

	public string Key { get; set; }

	public string? Fallback { get; set; }

	public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key, Fallback);
}
