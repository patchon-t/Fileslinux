// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Files.App.Linux.Models;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using AvPath = Avalonia.Controls.Shapes.Path;

namespace Files.App.Linux.Controls;

/// <summary>
/// Draws the icon for a <see cref="FileItem"/>: a thumbnail when available, the two-tone Windows 11 style folder,
/// or a page with a colored file-type glyph.
/// </summary>
public sealed class ItemIcon : ContentControl
{
	public static readonly StyledProperty<FileItem?> ItemProperty = AvaloniaProperty.Register<ItemIcon, FileItem?>(nameof(Item));

	public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<ItemIcon, double>(nameof(IconSize), 20);

	private static readonly IBrush FolderBack = new SolidColorBrush(Color.Parse("#E8A30C"));
	private static readonly IBrush FolderFront = new LinearGradientBrush
	{
		StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
		EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
		GradientStops = { new GradientStop(Color.Parse("#FFD86B"), 0), new GradientStop(Color.Parse("#FFC537"), 1) },
	};
	private static readonly IBrush PageFill = new SolidColorBrush(Color.Parse("#FDFDFD"));
	private static readonly IBrush PageStroke = new SolidColorBrush(Color.Parse("#9E9E9E"));
	private static readonly IBrush PageFold = new SolidColorBrush(Color.Parse("#E4E4E4"));
	private static readonly Geometry FolderBackGeometry = Geometry.Parse("M4,7 H17 L21,11 H44 A2,2 0 0 1 46,13 V38 A2,2 0 0 1 44,40 H4 A2,2 0 0 1 2,38 V9 A2,2 0 0 1 4,7 Z");
	private static readonly Geometry FolderFrontGeometry = Geometry.Parse("M2,17 A2.5,2.5 0 0 1 4.5,14.5 H43.5 A2.5,2.5 0 0 1 46,17 V38 A2,2 0 0 1 44,40 H4 A2,2 0 0 1 2,38 Z");
	private static readonly Geometry PageGeometry = Geometry.Parse("M9,2 H29 L41,14 V44 A2,2 0 0 1 39,46 H9 A2,2 0 0 1 7,44 V4 A2,2 0 0 1 9,2 Z");
	private static readonly Geometry FoldGeometry = Geometry.Parse("M29,2 V12 A2,2 0 0 0 31,14 H41 Z");

	private FileItem? _subscribed;

	public FileItem? Item
	{
		get => GetValue(ItemProperty);
		set => SetValue(ItemProperty, value);
	}

	public double IconSize
	{
		get => GetValue(IconSizeProperty);
		set => SetValue(IconSizeProperty, value);
	}

	public ItemIcon()
	{
		HorizontalContentAlignment = HorizontalAlignment.Center;
		VerticalContentAlignment = VerticalAlignment.Center;
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == ItemProperty)
		{
			if (_subscribed is not null)
				_subscribed.PropertyChanged -= OnItemPropertyChanged;
			_subscribed = Item;
			if (_subscribed is not null)
				_subscribed.PropertyChanged += OnItemPropertyChanged;
			Rebuild();
		}
		else if (change.Property == IconSizeProperty)
		{
			Rebuild();
		}
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromVisualTree(e);
		if (_subscribed is not null)
			_subscribed.PropertyChanged -= OnItemPropertyChanged;
		_subscribed = null;
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		if (_subscribed is null && Item is not null)
		{
			_subscribed = Item;
			_subscribed.PropertyChanged += OnItemPropertyChanged;
			Rebuild();
		}
	}

	private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(FileItem.Thumbnail) or nameof(FileItem.IsCut))
			Rebuild();
	}

	private void Rebuild()
	{
		var item = Item;
		var size = IconSize;
		Width = size;
		Height = size;
		if (item is null)
		{
			Content = null;
			return;
		}

		Opacity = item.IsCut ? 0.5 : 1;

		if (item.Thumbnail is { } bmp)
		{
			Content = new Border
			{
				CornerRadius = new CornerRadius(size >= 48 ? 4 : 2),
				ClipToBounds = true,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Child = new Image { Source = bmp, Stretch = Stretch.Uniform, MaxWidth = size, MaxHeight = size },
			};
			return;
		}

		if (item.IsDirectory)
		{
			Content = BuildFolder(size, item.IsSymlink);
			return;
		}

		if (size < 32)
		{
			Content = new SymbolIcon
			{
				Symbol = item.Icon,
				IconVariant = IconVariant.Filled,
				FontSize = size,
				Foreground = item.IconBrush,
			};
			return;
		}

		Content = BuildPage(size, item);
	}

	public static Control BuildFolder(double size, bool isLink = false)
	{
		var canvas = new Canvas { Width = 48, Height = 48 };
		canvas.Children.Add(new AvPath { Data = FolderBackGeometry, Fill = FolderBack });
		canvas.Children.Add(new AvPath { Data = FolderFrontGeometry, Fill = FolderFront });
		if (isLink)
		{
			var arrow = new SymbolIcon { Symbol = Symbol.ArrowUpRight, IconVariant = IconVariant.Filled, FontSize = 14, Foreground = Brushes.Black };
			Canvas.SetLeft(arrow, 4);
			Canvas.SetTop(arrow, 24);
			canvas.Children.Add(arrow);
		}
		return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
	}

	private static Control BuildPage(double size, FileItem item)
	{
		var canvas = new Canvas { Width = 48, Height = 48 };
		canvas.Children.Add(new AvPath { Data = PageGeometry, Fill = PageFill, Stroke = PageStroke, StrokeThickness = 1.2 });
		canvas.Children.Add(new AvPath { Data = FoldGeometry, Fill = PageFold, Stroke = PageStroke, StrokeThickness = 1.2 });
		var glyph = new SymbolIcon { Symbol = item.Icon, IconVariant = IconVariant.Filled, FontSize = 20, Foreground = item.IconBrush, Width = 22, Height = 22 };
		Canvas.SetLeft(glyph, 13);
		Canvas.SetTop(glyph, 21);
		canvas.Children.Add(glyph);
		return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
	}
}
