// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Files.App.Linux.Services;
using Files.App.Linux.Services.Icons;
using AvPath = Avalonia.Controls.Shapes.Path;

namespace Files.App.Linux.Controls;

/// <summary>
/// Drive icons in the Windows 11 style: a grey drive with a green activity light (a USB stick for removable
/// drives). The system drive gets a badge, like the Windows logo on C: — here the distribution's logo
/// (Debian, Ubuntu...) from /etc/os-release, or Tux when there is none.
/// </summary>
public static class DriveArt
{
	private static readonly IBrush TopFill = new LinearGradientBrush
	{
		StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
		EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
		GradientStops = { new GradientStop(Color.Parse("#F7F7F7"), 0), new GradientStop(Color.Parse("#E2E2E2"), 1) },
	};
	private static readonly IBrush FrontFill = new LinearGradientBrush
	{
		StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
		EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
		GradientStops = { new GradientStop(Color.Parse("#D2D2D2"), 0), new GradientStop(Color.Parse("#ADADAD"), 1) },
	};
	private static readonly IBrush Edge = new SolidColorBrush(Color.Parse("#8C8C8C"));
	private static readonly IBrush Slot = new SolidColorBrush(Color.Parse("#9A9A9A"));
	private static readonly IBrush Led = new SolidColorBrush(Color.Parse("#3BD16F"));
	private static readonly IBrush UsbBody = new LinearGradientBrush
	{
		StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
		EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
		GradientStops = { new GradientStop(Color.Parse("#6B6B6B"), 0), new GradientStop(Color.Parse("#3F3F3F"), 1) },
	};
	private static readonly IBrush UsbMetal = new SolidColorBrush(Color.Parse("#D9D9D9"));

	// Drive seen slightly from above: top face, front face, slot line, activity light.
	private static readonly Geometry DriveTop = Geometry.Parse("M7,14 L41,14 L47,24 L1,24 Z");
	private static readonly Geometry DriveFront = Geometry.Parse("M1,24 H47 V36.5 A3,3 0 0 1 44,39.5 H4 A3,3 0 0 1 1,36.5 Z");
	private static readonly Geometry UsbBodyGeometry = Geometry.Parse("M1,17 A3,3 0 0 1 4,14 H33 V38 H4 A3,3 0 0 1 1,35 Z");
	private static readonly Geometry UsbConnector = Geometry.Parse("M33,18 H47 V34 H33 Z");

	// Simplified Tux, used when the distribution has no logo.
	private static readonly Geometry TuxBody = Geometry.Parse("M12,1 C7,1 5.5,5 5.5,9 C5.5,11 4,13 3,16 C2,19 3,22 6,22.5 H18 C21,22 22,19 21,16 C20,13 18.5,11 18.5,9 C18.5,5 17,1 12,1 Z");
	private static readonly Geometry TuxBelly = Geometry.Parse("M12,8 C9,8 7.5,11 7.5,14.5 C7.5,18 9.5,21 12,21 C14.5,21 16.5,18 16.5,14.5 C16.5,11 15,8 12,8 Z");
	private static readonly Geometry TuxBeak = Geometry.Parse("M10,7.5 H14 L12,9.8 Z");
	private static readonly Geometry TuxFeet = Geometry.Parse("M3,21.5 C3,20 6,19.5 9,21.5 C8,23.5 4,23.5 3,21.5 Z M21,21.5 C21,20 18,19.5 15,21.5 C16,23.5 20,23.5 21,21.5 Z");

	/// <summary>Builds the drive icon at <paramref name="size"/> pixels.</summary>
	public static Control Build(double size, bool isRemovable, bool isSystem)
	{
		var canvas = new Canvas { Width = 48, Height = 48 };
		if (isRemovable)
		{
			canvas.Children.Add(new AvPath { Data = UsbConnector, Fill = UsbMetal, Stroke = Edge, StrokeThickness = 1 });
			canvas.Children.Add(new Rectangle { Width = 3, Height = 3, Fill = Edge, [Canvas.LeftProperty] = 38.5, [Canvas.TopProperty] = 21.0 });
			canvas.Children.Add(new Rectangle { Width = 3, Height = 3, Fill = Edge, [Canvas.LeftProperty] = 38.5, [Canvas.TopProperty] = 28.0 });
			canvas.Children.Add(new AvPath { Data = UsbBodyGeometry, Fill = UsbBody });
			canvas.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = Led, [Canvas.LeftProperty] = 6, [Canvas.TopProperty] = 24 });
		}
		else
		{
			canvas.Children.Add(new AvPath { Data = DriveTop, Fill = TopFill, Stroke = Edge, StrokeThickness = 0.8 });
			canvas.Children.Add(new AvPath { Data = DriveFront, Fill = FrontFill, Stroke = Edge, StrokeThickness = 0.8 });
			canvas.Children.Add(new Line { StartPoint = new Point(15, 32), EndPoint = new Point(43, 32), Stroke = Slot, StrokeThickness = 1.2 });
			canvas.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = Led, [Canvas.LeftProperty] = 5.5, [Canvas.TopProperty] = 30 });
		}

		if (isSystem)
		{
			Control badge = SystemLogo() is { } logo
				? new Image { Source = logo, Width = 22, Height = 22, Stretch = Stretch.Uniform }
				: BuildTux();
			Canvas.SetLeft(badge, -1);
			Canvas.SetTop(badge, -2);
			canvas.Children.Add(badge);
		}

		return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
	}

	private static Control BuildTux()
	{
		var tux = new Canvas { Width = 24, Height = 24 };
		tux.Children.Add(new AvPath { Data = TuxBody, Fill = new SolidColorBrush(Color.Parse("#1E1E1E")) });
		tux.Children.Add(new AvPath { Data = TuxBelly, Fill = Brushes.White });
		tux.Children.Add(new Ellipse { Width = 2.6, Height = 3.2, Fill = Brushes.White, [Canvas.LeftProperty] = 8.8, [Canvas.TopProperty] = 3.8 });
		tux.Children.Add(new Ellipse { Width = 2.6, Height = 3.2, Fill = Brushes.White, [Canvas.LeftProperty] = 12.6, [Canvas.TopProperty] = 3.8 });
		tux.Children.Add(new Ellipse { Width = 1.3, Height = 1.6, Fill = Brushes.Black, [Canvas.LeftProperty] = 9.8, [Canvas.TopProperty] = 5.0 });
		tux.Children.Add(new Ellipse { Width = 1.3, Height = 1.6, Fill = Brushes.Black, [Canvas.LeftProperty] = 13.2, [Canvas.TopProperty] = 5.0 });
		var orange = new SolidColorBrush(Color.Parse("#F5A623"));
		tux.Children.Add(new AvPath { Data = TuxBeak, Fill = orange });
		tux.Children.Add(new AvPath { Data = TuxFeet, Fill = orange });
		return new Viewbox { Width = 22, Height = 22, Child = tux };
	}

	private static bool _logoSearched;
	private static IImage? _logo;

	/// <summary>The distribution's logo: LOGO= from /etc/os-release, else "&lt;ID&gt;-logo" (e.g. debian-logo).</summary>
	public static IImage? SystemLogo()
	{
		if (_logoSearched)
			return _logo;
		_logoSearched = true;
		try
		{
			var names = new List<string>();
			string? id = null;
			foreach (var file in new[] { "/etc/os-release", "/usr/lib/os-release" })
			{
				if (!File.Exists(file))
					continue;
				foreach (var line in File.ReadLines(file))
				{
					if (line.StartsWith("LOGO=", StringComparison.Ordinal))
						names.Add(line[5..].Trim('"'));
					else if (line.StartsWith("ID=", StringComparison.Ordinal))
						id = line[3..].Trim('"');
				}
				break;
			}
			if (id is not null)
				names.Add(id + "-logo");
			names.Add("distributor-logo");

			foreach (var name in names.Where(n => n.Length > 0))
			{
				var path = new[] { ".svg", ".png" }.Select(ext => System.IO.Path.Combine("/usr/share/pixmaps", name + ext)).FirstOrDefault(File.Exists)
					?? IconTheme.Find("hicolor")?.Lookup([name], 64);
				if (path is not null && IconService.LoadImage(path, 64) is { } image)
					return _logo = image;
			}
		}
		catch (Exception)
		{
			// No logo: Tux is used instead.
		}
		return _logo;
	}

	/// <summary>Builds the icon for a drive card or sidebar drive (parameter: icon size in pixels).</summary>
	public static readonly IValueConverter Converter = new FuncValueConverter<object?, object?, Control?>((value, parameter) =>
	{
		var size = double.TryParse(parameter as string, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 40;
		return value switch
		{
			ViewModels.DriveCard d => Build(size, d.IsRemovable, d.IsRoot),
			Models.SidebarItem { Drive: { } info } item => Build(size, info.IsRemovable, item.Path == "/"),
			_ => null,
		};
	});
}
