// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Files.App.Linux.Services;

namespace Files.App.Linux.ViewModels;

/// <summary>An ongoing or finished file operation shown in the status center.</summary>
public sealed partial class OperationItem : ObservableObject
{
	public required string Title { get; init; }

	public CancellationTokenSource Cancellation { get; } = new();

	[ObservableProperty]
	public partial double Percent { get; set; }

	[ObservableProperty]
	public partial bool IsIndeterminate { get; set; } = true;

	[ObservableProperty]
	public partial string Status { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsRunning { get; set; } = true;

	[ObservableProperty]
	public partial bool IsFailed { get; set; }

	public void Report(OperationProgress p)
	{
		IsIndeterminate = p.TotalBytes == 0 && p.TotalItems == 0;
		Percent = p.Percent;
		Status = p.TotalBytes > 0
			? $"{FormatHelpers.FormatSize(p.DoneBytes)} / {FormatHelpers.FormatSize(p.TotalBytes)}"
			: $"{p.DoneItems} / {p.TotalItems}";
	}

	[RelayCommand]
	private void Cancel() => Cancellation.Cancel();
}

public sealed partial class OperationsViewModel : ObservableObject
{
	public ObservableCollection<OperationItem> Items { get; } = [];

	[ObservableProperty]
	public partial bool HasItems { get; set; }

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	[ObservableProperty]
	public partial double OverallPercent { get; set; }

	public OperationItem Start(string title)
	{
		var op = new OperationItem { Title = title };
		op.PropertyChanged += (_, _) => Update();
		Items.Insert(0, op);
		while (Items.Count > 20)
			Items.RemoveAt(Items.Count - 1);
		Update();
		return op;
	}

	public void Complete(OperationItem op, string? error = null)
	{
		op.IsRunning = false;
		op.IsIndeterminate = false;
		op.Percent = 100;
		if (error is not null)
		{
			op.IsFailed = true;
			op.Status = error;
		}
		else if (op.Cancellation.IsCancellationRequested)
		{
			op.Status = Loc.Get("Canceled", "Canceled");
		}
		else
		{
			op.Status = Loc.Get("Completed", "Completed");
		}
		Update();
	}

	[RelayCommand]
	private void ClearFinished()
	{
		foreach (var op in Items.Where(i => !i.IsRunning).ToList())
			Items.Remove(op);
		Update();
	}

	private void Update()
	{
		HasItems = Items.Count > 0;
		var running = Items.Where(i => i.IsRunning).ToList();
		IsBusy = running.Count > 0;
		OverallPercent = running.Count == 0 ? 0 : running.Average(i => i.Percent);
	}
}
