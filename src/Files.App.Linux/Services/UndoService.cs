// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Linux.Services;

/// <summary>An operation that can be undone and redone (Ctrl+Z / Ctrl+Y), like the Windows app's storage history.</summary>
public sealed record UndoAction(string Description, Func<Task> Undo, Func<Task> Redo);

/// <summary>
/// Undo/redo history shared by all windows. Covers rename, new folder/file, copy, move and move to trash.
/// Permanent deletes can't be undone and are not recorded.
/// </summary>
public static class UndoService
{
	private const int Limit = 50;
	private static readonly LinkedList<UndoAction> UndoStack = new();
	private static readonly Stack<UndoAction> RedoStack = new();

	public static event EventHandler? Changed;

	public static bool CanUndo => UndoStack.Count > 0;

	public static bool CanRedo => RedoStack.Count > 0;

	public static string? UndoDescription => UndoStack.Last?.Value.Description;

	public static string? RedoDescription => RedoStack.TryPeek(out var a) ? a.Description : null;

	public static void Record(UndoAction action)
	{
		UndoStack.AddLast(action);
		while (UndoStack.Count > Limit)
			UndoStack.RemoveFirst();
		RedoStack.Clear();
		Changed?.Invoke(null, EventArgs.Empty);
	}

	/// <summary>Undoes the last operation. Returns its description, or null when there was nothing to undo.</summary>
	public static async Task<string?> UndoAsync()
	{
		if (UndoStack.Last is not { } node)
			return null;
		UndoStack.RemoveLast();
		await node.Value.Undo();
		RedoStack.Push(node.Value);
		Changed?.Invoke(null, EventArgs.Empty);
		return node.Value.Description;
	}

	public static async Task<string?> RedoAsync()
	{
		if (!RedoStack.TryPop(out var action))
			return null;
		await action.Redo();
		UndoStack.AddLast(action);
		Changed?.Invoke(null, EventArgs.Empty);
		return action.Description;
	}

	public static void Clear()
	{
		UndoStack.Clear();
		RedoStack.Clear();
		Changed?.Invoke(null, EventArgs.Empty);
	}

	// ---------------- Ready-made actions ----------------

	public static UndoAction ForRename(string oldPath, string newPath) => new(
		$"Rename \"{Path.GetFileName(oldPath)}\"",
		() => Task.Run(() => FileOperationsService.Rename(newPath, Path.GetFileName(oldPath))),
		() => Task.Run(() => FileOperationsService.Rename(oldPath, Path.GetFileName(newPath))));

	/// <summary>Undoing a new item moves it to the trash; redoing restores it.</summary>
	public static UndoAction ForCreate(string path)
	{
		TrashEntry? trashed = null;
		return new(
			$"New \"{Path.GetFileName(path)}\"",
			() => Task.Run(() => trashed = TrashService.Default.Trash(path)),
			() => Task.Run(() =>
			{
				if (trashed is not null)
					TrashService.Default.Restore(trashed);
			}));
	}

	/// <summary>Moves are undone by moving each item back.</summary>
	public static UndoAction ForMove(IReadOnlyList<(string Source, string Target)> moved) => new(
		moved.Count == 1 ? $"Move \"{Path.GetFileName(moved[0].Source)}\"" : $"Move {moved.Count} items",
		() => Task.Run(() =>
		{
			foreach (var (source, target) in moved)
				MoveBack(target, source);
		}),
		() => Task.Run(() =>
		{
			foreach (var (source, target) in moved)
				MoveBack(source, target);
		}));

	/// <summary>Copies are undone by moving the copies to the trash.</summary>
	public static UndoAction ForCopy(IReadOnlyList<(string Source, string Target)> copied)
	{
		var trashed = new List<TrashEntry>();
		return new(
			copied.Count == 1 ? $"Copy \"{Path.GetFileName(copied[0].Source)}\"" : $"Copy {copied.Count} items",
			() => Task.Run(() =>
			{
				trashed.Clear();
				foreach (var (_, target) in copied)
				{
					if (FileOperationsService.Exists(target))
						trashed.Add(TrashService.Default.Trash(target));
				}
			}),
			() => Task.Run(() =>
			{
				foreach (var entry in trashed)
					TrashService.Default.Restore(entry);
				trashed.Clear();
			}));
	}

	/// <summary>Moving to the trash is undone by restoring the items.</summary>
	public static UndoAction ForTrash(IReadOnlyList<TrashEntry> entries)
	{
		var current = entries.ToList();
		return new(
			current.Count == 1 ? $"Delete \"{Path.GetFileName(current[0].OriginalPath)}\"" : $"Delete {current.Count} items",
			() => Task.Run(() =>
			{
				foreach (var entry in current)
					TrashService.Default.Restore(entry);
			}),
			() => Task.Run(() =>
			{
				var again = current.Select(e => TrashService.Default.Trash(e.OriginalPath)).ToList();
				current = again;
			}));
	}

	private static void MoveBack(string from, string to)
	{
		if (!FileOperationsService.Exists(from))
			return;
		var parent = Path.GetDirectoryName(to);
		if (parent is not null)
			Directory.CreateDirectory(parent);
		if (FileOperationsService.Exists(to))
			to = FileOperationsService.GetUniqueName(to);
		if (Directory.Exists(from) && new DirectoryInfo(from).LinkTarget is null)
			FileOperationsService.MoveDirectory(from, to);
		else
			File.Move(from, to);
	}
}
