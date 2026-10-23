// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;

namespace Files.App.Linux.Controls;

/// <summary>Shared column widths for the Details layout (header and every row read the same values).</summary>
public static class DetailsColumns
{
	public const int Name = 0;
	public const int Location = 1;
	public const int Modified = 2;
	public const int Type = 3;
	public const int Size = 4;
	public const int Count = 5;

	private static readonly double[] Widths = [320, 220, 170, 150, 100];

	public static bool ShowLocation { get; private set; }

	public static event EventHandler? Changed;

	public static double Get(int column) => column == Location && !ShowLocation ? 0 : Widths[column];

	public static void Set(int column, double width)
	{
		Widths[column] = Math.Clamp(width, 60, 1200);
		Changed?.Invoke(null, EventArgs.Empty);
	}

	public static void SetShowLocation(bool value)
	{
		if (ShowLocation == value)
			return;
		ShowLocation = value;
		Changed?.Invoke(null, EventArgs.Empty);
	}

	public static double TotalWidth => Enumerable.Range(0, Count).Sum(Get);
}

/// <summary>Lays out its children in the Details columns: Name, Location, Date modified, Type, Size.</summary>
public sealed class DetailsRowPanel : Panel
{
	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		DetailsColumns.Changed += OnColumnsChanged;
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromVisualTree(e);
		DetailsColumns.Changed -= OnColumnsChanged;
	}

	private void OnColumnsChanged(object? sender, EventArgs e) => InvalidateMeasure();

	protected override Size MeasureOverride(Size availableSize)
	{
		double height = 0;
		for (int i = 0; i < Children.Count; i++)
		{
			var w = i < DetailsColumns.Count ? DetailsColumns.Get(i) : 0;
			Children[i].Measure(new Size(w, availableSize.Height));
			height = Math.Max(height, Children[i].DesiredSize.Height);
		}
		return new Size(DetailsColumns.TotalWidth, height);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		double x = 0;
		for (int i = 0; i < Children.Count; i++)
		{
			var w = i < DetailsColumns.Count ? DetailsColumns.Get(i) : 0;
			Children[i].Arrange(new Rect(x, 0, w, finalSize.Height));
			x += w;
		}
		return new Size(Math.Max(finalSize.Width, x), finalSize.Height);
	}
}
