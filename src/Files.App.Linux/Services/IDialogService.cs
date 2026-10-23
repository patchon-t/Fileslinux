// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Linux.Services;

/// <summary>Shows in-window dialogs (rendered as an overlay, like ContentDialog on Windows).</summary>
public interface IDialogService
{
	Task<string?> PromptAsync(string title, string? message, string initialText, string primaryText, bool selectNameWithoutExtension = true);

	/// <summary>Returns whether the primary button was chosen and the state of the optional check box.</summary>
	Task<(bool Accepted, bool Checked)> ConfirmAsync(string title, string message, string primaryText, string? checkBoxText = null, bool isChecked = false, bool isDestructive = false);

	Task<ConflictResolution> ResolveConflictAsync(string path);

	Task ShowErrorAsync(string title, string message);

	void ShowProperties(IReadOnlyList<string> paths);

	void ShowSettings();
}

/// <summary>No-op dialogs, used before the window is ready and in tests.</summary>
public sealed class NullDialogService : IDialogService
{
	public Task<string?> PromptAsync(string title, string? message, string initialText, string primaryText, bool selectNameWithoutExtension = true) => Task.FromResult<string?>(initialText);

	public Task<(bool Accepted, bool Checked)> ConfirmAsync(string title, string message, string primaryText, string? checkBoxText = null, bool isChecked = false, bool isDestructive = false) => Task.FromResult((true, isChecked));

	public Task<ConflictResolution> ResolveConflictAsync(string path) => Task.FromResult(new ConflictResolution(ConflictChoice.KeepBoth, false));

	public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;

	public void ShowProperties(IReadOnlyList<string> paths)
	{
	}

	public void ShowSettings()
	{
	}
}
