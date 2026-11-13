// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using Files.App.Linux.Services;

namespace Files.App.Linux.Helpers;

/// <summary>
/// Motion tokens and helpers that reproduce the WinUI (Windows 11 Fluent) animations used by the Windows app.
/// See docs/linux-animations.md for where each one is used.
/// </summary>
public static class Motion
{
	// Durations (WinUI: ControlFasterAnimationDuration, ControlFastAnimationDuration, ControlNormalAnimationDuration).
	public static readonly TimeSpan Faster = TimeSpan.FromMilliseconds(83);
	public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(167);
	public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(250);
	public static readonly TimeSpan Entrance = TimeSpan.FromMilliseconds(300);

	/// <summary>Sidebar pane open/close in the Windows app (SidebarView.xaml, KeyTime 0:0:0.35).</summary>
	public static readonly TimeSpan Pane = TimeSpan.FromMilliseconds(350);

	/// <summary>WinUI "decelerate" curve used for entrances and the sidebar pane: KeySpline 0.1,0.9 0.2,1.0.</summary>
	public static readonly Easing Decelerate = new SplineEasing(0.1, 0.9, 0.2, 1.0);

	/// <summary>WinUI ControlFastOutSlowInKeySpline: 0,0 0,1.</summary>
	public static readonly Easing FastOutSlowIn = new SplineEasing(0.0, 0.0, 0.0, 1.0);

	/// <summary>WinUI exit curve: 1,0 1,1.</summary>
	public static readonly Easing Accelerate = new SplineEasing(1.0, 0.0, 1.0, 1.0);

	/// <summary>Distance items travel when they enter (EntranceThemeTransition-style).</summary>
	public const double EntranceOffset = 28;

	/// <summary>Delay between items in a staggered entrance.</summary>
	public static readonly TimeSpan StaggerStep = TimeSpan.FromMilliseconds(14);

	/// <summary>False when the user turned animations off in Settings.</summary>
	public static bool IsEnabled => SettingsService.Current.EnableAnimations;

	// Avalonia animates transforms through these properties; it creates the transform on the target as needed.
	private static (AvaloniaProperty, object)[] Translate(double x, double y) => [(TranslateTransform.XProperty, x), (TranslateTransform.YProperty, y)];

	private static (AvaloniaProperty, object)[] Scale(double s) => [(ScaleTransform.ScaleXProperty, s), (ScaleTransform.ScaleYProperty, s)];

	private static (AvaloniaProperty, object)[] With(double opacity, (AvaloniaProperty, object)[] transform) => [(Visual.OpacityProperty, opacity), .. transform];

	private static Animation Build(TimeSpan duration, Easing easing, TimeSpan delay, params (double Cue, (AvaloniaProperty Property, object Value)[] Setters)[] frames)
	{
		var animation = new Animation
		{
			Duration = duration,
			Easing = easing,
			Delay = delay,
			FillMode = FillMode.Both,
		};
		foreach (var (cue, setters) in frames)
		{
			var frame = new KeyFrame { Cue = new Cue(cue) };
			foreach (var (property, value) in setters)
				frame.Setters.Add(new Setter(property, value));
			animation.Children.Add(frame);
		}
		return animation;
	}

	private static async Task RunAsync(Animatable target, Animation animation, Action? done = null)
	{
		try
		{
			await animation.RunAsync(target);
		}
		catch (Exception ex)
		{
			// Animations are cosmetic; never let one break the UI.
			System.Diagnostics.Trace.TraceWarning($"Animation failed: {ex.Message}");
		}
		done?.Invoke();
	}

	/// <summary>Fade + slide in from below, like WinUI's EntranceThemeTransition.</summary>
	public static Task EntranceAsync(Visual target, TimeSpan? delay = null, double offsetX = 0, double offsetY = EntranceOffset)
	{
		if (!IsEnabled)
			return Task.CompletedTask;
		var animation = Build(Entrance, Decelerate, delay ?? TimeSpan.Zero,
			(0, With(0d, Translate(offsetX, offsetY))),
			(1, With(1d, Translate(0d, 0d))));
		return RunAsync(target, animation);
	}

	/// <summary>Staggered entrance for a set of items (the first ones animate, later ones just appear).</summary>
	public static void StaggeredEntrance(IEnumerable<Visual> items, int maxAnimated = 24)
	{
		if (!IsEnabled)
			return;
		int i = 0;
		foreach (var item in items)
		{
			if (i >= maxAnimated)
				break;
			_ = EntranceAsync(item, TimeSpan.FromTicks(StaggerStep.Ticks * i));
			i++;
		}
	}

	/// <summary>Simple fade in (content swaps, thumbnails).</summary>
	public static Task FadeInAsync(Visual target, TimeSpan? duration = null)
	{
		if (!IsEnabled)
			return Task.CompletedTask;
		var animation = Build(duration ?? Fast, FastOutSlowIn, TimeSpan.Zero,
			(0, [(Visual.OpacityProperty, 0d)]),
			(1, [(Visual.OpacityProperty, 1d)]));
		return RunAsync(target, animation);
	}

	/// <summary>ContentDialog open: scale 1.05 → 1 with fade (250 ms, decelerate).</summary>
	public static Task DialogInAsync(Visual card, Visual smoke)
	{
		if (!IsEnabled)
			return Task.CompletedTask;
		var smokeIn = Build(Fast, FastOutSlowIn, TimeSpan.Zero,
			(0, [(Visual.OpacityProperty, 0d)]),
			(1, [(Visual.OpacityProperty, 1d)]));
		var cardIn = Build(Normal, Decelerate, TimeSpan.Zero,
			(0, With(0d, Scale(1.05))),
			(1, With(1d, Scale(1d))));
		return Task.WhenAll(RunAsync(smoke, smokeIn), RunAsync(card, cardIn));
	}

	/// <summary>ContentDialog close: scale 1 → 1.05 with fade out (167 ms, accelerate).</summary>
	public static Task DialogOutAsync(Visual card, Visual smoke)
	{
		if (!IsEnabled)
			return Task.CompletedTask;
		var smokeOut = Build(Fast, FastOutSlowIn, TimeSpan.Zero,
			(0, [(Visual.OpacityProperty, 1d)]),
			(1, [(Visual.OpacityProperty, 0d)]));
		var cardOut = Build(Fast, Accelerate, TimeSpan.Zero,
			(0, With(1d, Scale(1d))),
			(1, With(0d, Scale(1.05))));
		return Task.WhenAll(RunAsync(smoke, smokeOut), RunAsync(card, cardOut));
	}

	/// <summary>Panes sliding in from the side (details pane), 250 ms decelerate.</summary>
	public static Task SlideInAsync(Visual target, double fromX)
	{
		if (!IsEnabled)
			return Task.CompletedTask;
		var animation = Build(Normal, Decelerate, TimeSpan.Zero,
			(0, With(0d, Translate(fromX, 0d))),
			(1, With(1d, Translate(0d, 0d))));
		return RunAsync(target, animation);
	}

	/// <summary>One clockwise turn, used by the refresh button (500 ms, fast-out-slow-in).</summary>
	public static Task SpinAsync(Visual target)
	{
		if (!IsEnabled)
			return Task.CompletedTask;
		var animation = Build(TimeSpan.FromMilliseconds(500), FastOutSlowIn, TimeSpan.Zero,
			(0, [(RotateTransform.AngleProperty, 0d)]),
			(1, [(RotateTransform.AngleProperty, 360d)]));
		animation.FillMode = FillMode.None;
		return RunAsync(target, animation);
	}

	/// <summary>Animates any double-valued property (used for the sidebar width).</summary>
	public static Task AnimateDoubleAsync(Animatable target, AvaloniaProperty<double> property, double from, double to, TimeSpan duration, Easing easing)
	{
		var animation = Build(duration, easing, TimeSpan.Zero,
			(0, [(property, from)]),
			(1, [(property, to)]));
		animation.FillMode = FillMode.None;
		return RunAsync(target, animation);
	}
}
