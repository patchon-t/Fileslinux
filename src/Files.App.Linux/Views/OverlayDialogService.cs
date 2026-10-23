// Copyright (c) Files Community
// Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Files.App.Linux.Helpers;
using Files.App.Linux.Services;

namespace Files.App.Linux.Views;

/// <summary>Shows dialogs as a centered card over a dimmed window, like WinUI's ContentDialog.</summary>
public sealed class OverlayDialogService(MainWindow window) : IDialogService
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private Action<object?, KeyEventArgs>? _keyHandler;

	/// <summary>Called by the window for every key press while a dialog is open (Enter = primary, Esc = cancel).</summary>
	public void HandleKey(KeyEventArgs e) => _keyHandler?.Invoke(this, e);

	private Panel Layer => window.DialogLayer;

	private Border Card => window.DialogCard;

	private async Task<T> ShowAsync<T>(string title, Control? body, IReadOnlyList<(string Text, T Result, bool IsPrimary, bool IsDestructive)> buttons, T cancelResult, Control? initialFocus = null, Func<T, bool>? validate = null)
	{
		await _gate.WaitAsync();
		try
		{
			var previousFocus = window.FocusManager?.GetFocusedElement() as IInputElement;
			var tcs = new TaskCompletionSource<T>();

			var content = new StackPanel { Margin = new Thickness(24, 24, 24, 24), Spacing = 12 };
			content.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
			if (body is not null)
				content.Children.Add(body);

			var footer = new Grid { Margin = new Thickness(24, 20), ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", buttons.Count))) };
			Button? primary = null;
			for (int i = 0; i < buttons.Count; i++)
			{
				var (text, result, isPrimary, isDestructive) = buttons[i];
				var button = new Button
				{
					Content = text,
					HorizontalAlignment = HorizontalAlignment.Stretch,
					HorizontalContentAlignment = HorizontalAlignment.Center,
					Margin = new Thickness(i == 0 ? 0 : 4, 0, i == buttons.Count - 1 ? 0 : 4, 0),
					MinWidth = 96,
				};
				if (isDestructive)
					button.Classes.Add("danger");
				else if (isPrimary)
					button.Classes.Add("accent");
				if (isPrimary)
					primary ??= button;
				button.Click += (_, _) =>
				{
					if (validate is null || validate(result))
						tcs.TrySetResult(result);
				};
				Grid.SetColumn(button, i);
				footer.Children.Add(button);
			}

			var root = new DockPanel { LastChildFill = true };
			var footerBorder = new Border { Child = footer }.WithResource(Border.BackgroundProperty, "App.Theme.DialogFooterBrush");
			DockPanel.SetDock(footerBorder, Dock.Bottom);
			root.Children.Add(footerBorder);
			root.Children.Add(content);

			Card.Child = root;
			Layer.IsVisible = true;

			void OnKey(object? s, KeyEventArgs e)
			{
				if (e.Key == Key.Escape)
				{
					tcs.TrySetResult(cancelResult);
					e.Handled = true;
				}
				else if (e.Key == Key.Enter && primary is not null && !(e.Source is TextBox { AcceptsReturn: true }))
				{
					var (_, result, _, _) = buttons.First(b => b.IsPrimary);
					if (validate is null || validate(result))
						tcs.TrySetResult(result);
					e.Handled = true;
				}
			}

			_keyHandler = OnKey;
			Dispatcher.UIThread.Post(() => (initialFocus ?? primary ?? (Control?)footer.Children.OfType<Button>().FirstOrDefault())?.Focus(), DispatcherPriority.Background);

			var value = await tcs.Task;
			_keyHandler = null;
			Layer.IsVisible = false;
			Card.Child = null;
			previousFocus?.Focus();
			return value;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<string?> PromptAsync(string title, string? message, string initialText, string primaryText, bool selectNameWithoutExtension = true)
	{
		var panel = new StackPanel { Spacing = 8, Width = 400 };
		if (message is not null)
			panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
		var box = new TextBox { Text = initialText };
		var error = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, FontSize = 12 }.WithResource(TextBlock.ForegroundProperty, "App.Theme.CriticalBrush");
		panel.Children.Add(box);
		panel.Children.Add(error);

		box.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
		{
			if (!box.IsVisible)
				return;
			box.Focus();
			var dot = initialText.LastIndexOf('.');
			box.SelectionStart = 0;
			box.SelectionEnd = selectNameWithoutExtension && dot > 0 ? dot : initialText.Length;
		}, DispatcherPriority.Background);

		bool Validate(bool accepted)
		{
			if (!accepted)
				return true;
			try
			{
				FileOperationsService.ValidateName(box.Text ?? string.Empty);
				return true;
			}
			catch (Exception ex)
			{
				error.Text = ex.Message;
				error.IsVisible = true;
				return false;
			}
		}

		var ok = await ShowAsync(title, panel, [(primaryText, true, true, false), (Loc.Get("Cancel", "Cancel"), false, false, false)], false, box, Validate);
		return ok ? box.Text?.Trim() : null;
	}

	public async Task<(bool Accepted, bool Checked)> ConfirmAsync(string title, string message, string primaryText, string? checkBoxText = null, bool isChecked = false, bool isDestructive = false)
	{
		var panel = new StackPanel { Spacing = 12, MaxWidth = 460 };
		panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
		CheckBox? check = null;
		if (checkBoxText is not null)
		{
			check = new CheckBox { Content = checkBoxText, IsChecked = isChecked };
			panel.Children.Add(check);
		}
		var ok = await ShowAsync(title, panel, [(primaryText, true, true, isDestructive), (Loc.Get("Cancel", "Cancel"), false, false, false)], false);
		return (ok, check?.IsChecked == true);
	}

	public async Task<ConflictResolution> ResolveConflictAsync(string path)
	{
		var panel = new StackPanel { Spacing = 12, MaxWidth = 460 };
		panel.Children.Add(new TextBlock { Text = $"\"{Path.GetFileName(path)}\" already exists in \"{Path.GetDirectoryName(path)}\".", TextWrapping = TextWrapping.Wrap });
		var all = new CheckBox { Content = Loc.Get("ApplyToAll", "Do this for all conflicts") };
		panel.Children.Add(all);
		var choice = await ShowAsync(Loc.Get("ItemAlreadyExistsDialogTitle", "Item already exists"), panel,
		[
			(Loc.Get("ReplaceExisting", "Replace existing"), ConflictChoice.Replace, true, false),
			(Loc.Get("GenerateNewName", "Generate new name"), ConflictChoice.KeepBoth, false, false),
			(Loc.Get("Skip", "Skip"), ConflictChoice.Skip, false, false),
			(Loc.Get("Cancel", "Cancel"), ConflictChoice.Cancel, false, false),
		], ConflictChoice.Cancel);
		return new ConflictResolution(choice, all.IsChecked == true);
	}

	public async Task ShowErrorAsync(string title, string message)
	{
		var text = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
		await ShowAsync(title, text, [(Loc.Get("OK", "OK"), true, true, false)], true);
	}

	public void ShowProperties(IReadOnlyList<string> paths)
	{
		var properties = new PropertiesWindow(paths);
		properties.Show(window);
	}

	public void ShowSettings()
	{
		var settings = new SettingsWindow();
		_ = settings.ShowDialog(window);
	}
}
