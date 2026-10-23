// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Linux.Services;

namespace Files.App.Linux.Tests;

[TestClass]
public sealed class FileOperationsTests
{
	[TestMethod]
	public void GetUniqueName_AddsCounterBeforeExtension()
	{
		var dir = TestEnvironment.NewTempDir();
		var file = Path.Combine(dir, "report.txt");
		File.WriteAllText(file, "x");
		Assert.AreEqual(Path.Combine(dir, "report (2).txt"), FileOperationsService.GetUniqueName(file));

		File.WriteAllText(Path.Combine(dir, "report (2).txt"), "x");
		Assert.AreEqual(Path.Combine(dir, "report (3).txt"), FileOperationsService.GetUniqueName(file));
	}

	[TestMethod]
	public void GetUniqueName_KeepsCompoundExtensions()
	{
		var dir = TestEnvironment.NewTempDir();
		var file = Path.Combine(dir, "backup.tar.gz");
		File.WriteAllText(file, "x");
		Assert.AreEqual(Path.Combine(dir, "backup (2).tar.gz"), FileOperationsService.GetUniqueName(file));
	}

	[TestMethod]
	public void CreateRenameAndDelete()
	{
		var dir = TestEnvironment.NewTempDir();
		var folder = FileOperationsService.CreateFolder(dir, "New folder");
		Assert.IsTrue(Directory.Exists(folder));
		Assert.ThrowsExactly<IOException>(() => FileOperationsService.CreateFolder(dir, "New folder"));
		Assert.ThrowsExactly<ArgumentException>(() => FileOperationsService.CreateFolder(dir, "a/b"));

		var file = FileOperationsService.CreateFile(folder, "a.txt");
		var renamed = FileOperationsService.Rename(file, "b.txt");
		Assert.IsFalse(File.Exists(file));
		Assert.IsTrue(File.Exists(renamed));

		FileOperationsService.DeletePermanently(folder);
		Assert.IsFalse(Directory.Exists(folder));
	}

	[TestMethod]
	public async Task Copy_RecursesAndReportsProgress()
	{
		var src = TestEnvironment.NewTempDir();
		var dst = TestEnvironment.NewTempDir();
		Directory.CreateDirectory(Path.Combine(src, "photos", "2024"));
		File.WriteAllBytes(Path.Combine(src, "photos", "2024", "a.jpg"), new byte[5000]);
		File.WriteAllText(Path.Combine(src, "photos", "b.txt"), "hello");

		OperationProgress? last = null;
		var created = await FileOperationsService.TransferAsync([Path.Combine(src, "photos")], dst, move: false,
			_ => Task.FromResult(new ConflictResolution(ConflictChoice.Cancel, false)),
			new SyncProgress<OperationProgress>(p => last = p));

		Assert.HasCount(1, created);
		Assert.IsTrue(File.Exists(Path.Combine(dst, "photos", "2024", "a.jpg")));
		Assert.AreEqual("hello", File.ReadAllText(Path.Combine(dst, "photos", "b.txt")));
		Assert.IsTrue(Directory.Exists(Path.Combine(src, "photos")), "Copy must keep the source.");
		Assert.IsNotNull(last);
		Assert.AreEqual(5005, last.TotalBytes);
		Assert.AreEqual(5005, last.DoneBytes);
	}

	[TestMethod]
	public async Task Move_RemovesSource()
	{
		var src = TestEnvironment.NewTempDir();
		var dst = TestEnvironment.NewTempDir();
		var file = Path.Combine(src, "doc.md");
		File.WriteAllText(file, "# hi");

		await FileOperationsService.TransferAsync([file], dst, move: true, _ => Task.FromResult(new ConflictResolution(ConflictChoice.Cancel, false)));

		Assert.IsFalse(File.Exists(file));
		Assert.IsTrue(File.Exists(Path.Combine(dst, "doc.md")));
	}

	[TestMethod]
	public async Task CopyIntoSameFolder_CreatesNumberedCopy()
	{
		var dir = TestEnvironment.NewTempDir();
		var file = Path.Combine(dir, "notes.txt");
		File.WriteAllText(file, "x");

		var created = await FileOperationsService.TransferAsync([file], dir, move: false, _ => throw new InvalidOperationException("No conflict prompt expected"));

		Assert.AreEqual(Path.Combine(dir, "notes (2).txt"), created.Single());
	}

	[TestMethod]
	public async Task Conflict_HonorsResolution()
	{
		var src = TestEnvironment.NewTempDir();
		var dst = TestEnvironment.NewTempDir();
		File.WriteAllText(Path.Combine(src, "a.txt"), "new");
		File.WriteAllText(Path.Combine(dst, "a.txt"), "old");

		await FileOperationsService.TransferAsync([Path.Combine(src, "a.txt")], dst, false, _ => Task.FromResult(new ConflictResolution(ConflictChoice.Skip, false)));
		Assert.AreEqual("old", File.ReadAllText(Path.Combine(dst, "a.txt")));

		await FileOperationsService.TransferAsync([Path.Combine(src, "a.txt")], dst, false, _ => Task.FromResult(new ConflictResolution(ConflictChoice.KeepBoth, false)));
		Assert.AreEqual("new", File.ReadAllText(Path.Combine(dst, "a (2).txt")));

		await FileOperationsService.TransferAsync([Path.Combine(src, "a.txt")], dst, false, _ => Task.FromResult(new ConflictResolution(ConflictChoice.Replace, false)));
		Assert.AreEqual("new", File.ReadAllText(Path.Combine(dst, "a.txt")));
	}

	[TestMethod]
	public async Task CopyFolderIntoItself_Throws()
	{
		var dir = TestEnvironment.NewTempDir();
		var inner = Directory.CreateDirectory(Path.Combine(dir, "inner")).FullName;
		await Assert.ThrowsExactlyAsync<IOException>(() => FileOperationsService.TransferAsync([dir], inner, false, _ => Task.FromResult(new ConflictResolution(ConflictChoice.Cancel, false))));
	}

	[TestMethod]
	public async Task CompressAndExtractZip()
	{
		var dir = TestEnvironment.NewTempDir();
		var folder = Directory.CreateDirectory(Path.Combine(dir, "project")).FullName;
		File.WriteAllText(Path.Combine(folder, "readme.md"), "hello");
		Directory.CreateDirectory(Path.Combine(folder, "src"));
		File.WriteAllText(Path.Combine(folder, "src", "main.c"), "int main(){}");

		var zip = await FileOperationsService.CompressToZipAsync([folder], Path.Combine(dir, "project.zip"));
		Assert.IsTrue(File.Exists(zip));
		Assert.IsTrue(FileTypes.IsArchive(zip));

		var extracted = await FileOperationsService.ExtractAsync(zip);
		Assert.AreEqual("hello", File.ReadAllText(Path.Combine(extracted, "project", "readme.md")));
		Assert.AreEqual("int main(){}", File.ReadAllText(Path.Combine(extracted, "project", "src", "main.c")));
	}

	private sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
	{
		public void Report(T value) => action(value);
	}
}

[TestClass]
public sealed class TrashTests
{
	[TestMethod]
	public void TrashListRestoreAndEmpty()
	{
		var root = TestEnvironment.NewTempDir();
		var trashDir = Path.Combine(root, "Trash");
		var mounts = new List<MountEntry> { new("/", "ext4", "/dev/sda1", "rw") };
		var trash = new TrashService(trashDir, () => mounts);

		var docs = Directory.CreateDirectory(Path.Combine(root, "My Docs")).FullName;
		var file = Path.Combine(docs, "résumé 1.txt");
		File.WriteAllText(file, "cv");

		var entry = trash.Trash(file);
		Assert.IsFalse(File.Exists(file));
		Assert.IsTrue(File.Exists(Path.Combine(trashDir, "files", "résumé 1.txt")));

		var info = File.ReadAllText(Path.Combine(trashDir, "info", "résumé 1.txt.trashinfo"));
		StringAssert.StartsWith(info, "[Trash Info]\nPath=");
		StringAssert.Contains(info, "My%20Docs/r%C3%A9sum%C3%A9%201.txt");
		StringAssert.Contains(info, "DeletionDate=");

		// A second item with the same name gets a unique trash name.
		File.WriteAllText(file, "cv2");
		trash.Trash(file);

		var listed = trash.List();
		Assert.HasCount(2, listed);
		Assert.IsTrue(listed.All(e => e.OriginalPath == file));
		Assert.IsFalse(trash.IsEmpty());

		var restored = trash.Restore(entry);
		Assert.AreEqual(file, restored);
		Assert.IsTrue(File.Exists(file));
		Assert.HasCount(1, trash.List());

		trash.Empty();
		Assert.IsTrue(trash.IsEmpty());
		Assert.IsEmpty(trash.List());
	}

	[TestMethod]
	public void RestoreToOccupiedPath_GeneratesNewName()
	{
		var root = TestEnvironment.NewTempDir();
		var trash = new TrashService(Path.Combine(root, "Trash"), () => [new("/", "ext4", "/dev/sda1", "rw")]);
		var file = Path.Combine(root, "a.txt");
		File.WriteAllText(file, "1");
		var entry = trash.Trash(file);
		File.WriteAllText(file, "2");

		var restored = trash.Restore(entry);
		Assert.AreEqual(Path.Combine(root, "a (2).txt"), restored);
		Assert.AreEqual("1", File.ReadAllText(restored));
	}

	[TestMethod]
	public void ParseInfo_ResolvesRelativePathsAgainstTopDir()
	{
		var root = TestEnvironment.NewTempDir();
		var trashDir = Path.Combine(root, ".Trash-1000");
		Directory.CreateDirectory(Path.Combine(trashDir, "info"));
		var info = Path.Combine(trashDir, "info", "x.trashinfo");
		File.WriteAllText(info, "[Trash Info]\nPath=photos/x%20y.png\nDeletionDate=2024-05-01T10:20:30\n");

		var (path, date) = TrashService.ParseInfo(info, trashDir);
		Assert.AreEqual(Path.Combine(root, "photos", "x y.png"), path);
		Assert.AreEqual(new DateTime(2024, 5, 1, 10, 20, 30), date);
	}

	[TestMethod]
	public void EncodePath_KeepsSlashesAndEscapesOthers()
	{
		Assert.AreEqual("/home/u/a%20b%25.txt", TrashService.EncodePath("/home/u/a b%.txt"));
	}
}

[TestClass]
public sealed class ParsingTests
{
	[TestMethod]
	public void MountInfo_ParsesAndUnescapes()
	{
		var lines = new[]
		{
			"22 1 8:2 / / rw,relatime shared:1 - ext4 /dev/sda2 rw",
			"98 22 8:17 / /media/user/My\\040Disk rw,nosuid shared:50 - vfat /dev/sdb1 rw,uid=1000",
			"25 22 0:5 / /proc rw - proc proc rw",
		};
		var mounts = MountService.ParseMountInfo(lines);
		Assert.HasCount(3, mounts);
		Assert.AreEqual("/media/user/My Disk", mounts[1].MountPoint);
		Assert.AreEqual("vfat", mounts[1].FsType);
		Assert.AreEqual("/dev/sdb1", mounts[1].Source);
		Assert.AreEqual("/media/user/My Disk", MountService.GetMountPoint("/media/user/My Disk/photos/a.jpg", mounts));
		Assert.AreEqual("/", MountService.GetMountPoint("/home/user", mounts));
	}

	[TestMethod]
	public void UserDirs_ParsesXdgFile()
	{
		var dir = TestEnvironment.NewTempDir();
		var file = Path.Combine(dir, "user-dirs.dirs");
		File.WriteAllText(file, "# comment\nXDG_DESKTOP_DIR=\"$HOME/Bureau\"\nXDG_DOWNLOAD_DIR=\"$HOME/Téléchargements\"\nXDG_MUSIC_DIR=\"/data/music\"\n");
		var dirs = XdgPaths.ParseUserDirs(file, "/home/marie");
		Assert.AreEqual("/home/marie/Bureau", dirs["DESKTOP"]);
		Assert.AreEqual("/home/marie/Téléchargements", dirs["DOWNLOAD"]);
		Assert.AreEqual("/data/music", dirs["MUSIC"]);
	}

	[TestMethod]
	public void Clipboard_ParsesGnomeAndUriList()
	{
		var (paths, cut) = ClipboardService.ParseGnome("cut\nfile:///home/u/a%20b.txt\nfile:///home/u/c");
		Assert.IsTrue(cut);
		CollectionAssert.AreEqual(new[] { "/home/u/a b.txt", "/home/u/c" }, paths);

		var list = ClipboardService.ParseUriList("# comment\r\nfile:///tmp/x\r\nhttps://example.com\r\n");
		CollectionAssert.AreEqual(new[] { "/tmp/x" }, list);
	}

	[TestMethod]
	public void DesktopFile_ParsesAndSplitsExec()
	{
		var dir = TestEnvironment.NewTempDir();
		var file = Path.Combine(dir, "org.gnome.TextEditor.desktop");
		File.WriteAllText(file, "[Desktop Entry]\nType=Application\nName=Text Editor\nExec=gnome-text-editor --new-window %U\nMimeType=text/plain;text/markdown;\n\n[Desktop Action new]\nName=Ignored\n");
		var app = LauncherService.ParseDesktopFile(file, "org.gnome.TextEditor.desktop");
		Assert.IsNotNull(app);
		Assert.AreEqual("Text Editor", app.Name);
		CollectionAssert.AreEqual(new[] { "text/plain", "text/markdown" }, app.MimeTypes.ToArray());
		CollectionAssert.AreEqual(new[] { "gnome-text-editor", "--new-window", "%U" }, LauncherService.SplitCommand(app.Exec));
		CollectionAssert.AreEqual(new[] { "sh", "-c", "echo hi there" }, LauncherService.SplitCommand("sh -c \"echo hi there\""));
	}

	[TestMethod]
	public void SearchMatcher_SupportsWildcardsAndSubstrings()
	{
		var wildcard = FileSystemService.BuildMatcher("*.PNG");
		Assert.IsTrue(wildcard("photo.png"));
		Assert.IsFalse(wildcard("photo.png.bak"));
		var substring = FileSystemService.BuildMatcher("port");
		Assert.IsTrue(substring("Report.docx"));
		Assert.IsFalse(substring("Repo.docx"));
	}

	[TestMethod]
	public void Loc_FormatsIcuPlurals()
	{
		const string pattern = "{0, plural, one {# item selected} other {# items selected}}";
		Assert.AreEqual("1 item selected", Loc.FormatPattern(pattern, 1));
		Assert.AreEqual("5 items selected", Loc.FormatPattern(pattern, 5));
		Assert.AreEqual("Search results in Docs for cat", Loc.FormatPattern("Search results in {1} for {0}", "cat", "Docs"));
	}

	[TestMethod]
	public void Loc_ReadsWindowsTranslations()
	{
		Loc.Load("de-DE");
		Assert.AreEqual("de-DE", Loc.CurrentLanguage);
		Assert.AreNotEqual("Cut", Loc.Get("Cut"));
		Loc.Load("en-US");
		Assert.AreEqual("Cut", Loc.Get("Cut"));
		Assert.AreEqual("Fallback text", Loc.Get("NoSuchKey_xyz", "Fallback text"));
	}

	[TestMethod]
	public void FileTypes_DescribeCommonFiles()
	{
		Assert.AreEqual(FileKind.Image, FileTypes.Get(".JPG").Kind);
		Assert.AreEqual(FileKind.Archive, FileTypes.Get(".tgz").Kind);
		Assert.AreEqual(FileKind.Package, FileTypes.Get(".deb").Kind);
		Assert.AreEqual("XYZ file", FileTypes.Get(".xyz").Description);
	}
}
