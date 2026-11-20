# Files for Linux: everything we did in this chat

This file explains the whole conversation: what you asked for, what was built, how it works, and how to build, install and use it.

> **สรุปสั้น ๆ (ภาษาไทย):** เราพอร์ตโปรแกรม Files (ตัวจัดการไฟล์ของ Windows) มาเป็นแอป Linux ด้วย Avalonia ให้หน้าตาและการทำงานเหมือนเวอร์ชัน Windows ให้มากที่สุด ทำแพ็กเกจ `.deb` สำหรับ Debian และเพิ่มฟีเจอร์ตามที่ขอทีละข้อ ได้แก่ แอนิเมชัน แท็บในแถบชื่อเรื่อง ลากเลือกไฟล์ ดึงแท็บออกเป็นหน้าต่างใหม่ คีย์ลัด มุมมองแยกตามโฟลเดอร์ ไอคอนแบบกำหนดเอง ช่องที่อยู่ที่พิมพ์คำสั่งได้ การจัดกลุ่มตามวันที่ในโฟลเดอร์ Downloads และไอคอนไดรฟ์แบบ Windows 11 ผ่านการทดสอบทั้งหมด 60 รายการ

---

## 1. The goal

The **Files** app in this repository is a WinUI 3 file manager, and WinUI 3 only runs on Windows. You asked for a Linux version that looks and works like the Windows one, and you named **Debian** as the target.

**Why Avalonia?** Avalonia is a cross-platform UI framework that uses the same C# and XAML model as WinUI. That let the port:

- reuse the Windows app's **translation files** (`src/Files.App/Strings/*/Resources.resw`), so every language works;
- use the same **Fluent UI icons**, colors, spacing and animation timings;
- keep the code style close to the Windows app.

The port lives in **`src/Files.App.Linux`**. It has its own solution, `Files.Linux.slnx`, so the Windows build (`Files.slnx`) is not affected.

---

## 2. Timeline: every request and what was done

| # | What you asked | What was done | Commit | .deb |
|---|---|---|---|---|
| 1 | "make a port for linux, ui and all like windows" plus "debian" | Built the whole Avalonia app: tabs, sidebar, Home page, Details/List/Grid layouts, file operations, freedesktop Trash, drives, settings, properties, the Status center, search and translations. Added the `.deb` build script, a `.desktop` entry, AppStream metadata, icons, a CI workflow and tests. | `9621ba0` | 1.0.0 |
| 2 | apt error: `libicu76/74/72/... not installable` | `libicu` moved from **Depends** to **Recommends**. The `files` launcher script now starts the app in invariant globalization mode when no ICU is installed. Package compression changed from zstd to **xz** so older dpkg versions can install it. | `5a3fe0f` | 1.0.1 |
| 3 | "redesign readme md" | Rewrote `.github/README.md` to cover both the Windows and Linux versions, with screenshots and install steps. | `7516fda` | — |
| 4 | "animation for ui like windows ... make it as ref" | Copied the WinUI motion tokens (durations and easing curves) into `Helpers/Motion.cs` and `Styles/Motion.axaml`. Added entrance animations: page content rises in, the sidebar slides, dialogs scale in, menus slide down and hover colors fade. Animations can be turned off in Settings. Wrote **`docs/linux-animations.md`** as the reference. | `780e763` | 1.0.2 |
| 5 | "where is .deb" | Pointed you to `artifacts/linux/` and the CI artifacts. | — | — |
| 6 | Top bar should merge with the title bar, some dropdowns don't work, the refresh icon should animate | **Tabs now sit in the title bar**, with Windows-style minimize, maximize and close buttons (a custom title bar with drag-to-move, double-click to maximize and resize edges). Fixed the **empty toolbar menus**: they are now built when clicked. The **refresh icon spins** when clicked. A "use system title bar" option was added to Settings. | `816903d` | 1.0.3 |
| 7 | Windows animated icons, drag-select box, drag onto folders, tab tear-out, shortcuts, and a **per-folder view** | Added animated toolbar icons; a **drag-select (marquee) box**; **drag and drop onto folders** with a "Move to …" highlight; **dragging a tab out into its own window**, which keeps its history; **undo/redo**; and copy/cut/paste/rename shortcuts. **Each folder remembers its own layout and sort**: a new folder uses the default, and changing one folder affects only that folder. | `56bd634` | 1.0.4 |
| 8 | A file to customize icons across apps, and fix Details column resizing ("Try again") | **Icon packs**: a single `icons.json` file, importable from a `.zip`, that can also be installed as a **system icon theme** so other apps use it too. Any installed icon theme (Papirus, Adwaita…) can also be chosen. Fixed **Details column resizing**: a hidden column's grip had been on top of the visible one, and the cells are now clipped. Column widths are saved. Wrote **`docs/linux-icons.md`**. | `3cdd24b` | 1.0.5 |
| 9 | Editable address bar that runs commands, like typing `cmd` on Windows | Click the address bar (or press Ctrl+L, Alt+D or F4) to type. A path navigates there. A file opens. **`cmd` / `terminal` / `powershell` / `wt` opens a terminal in the current folder.** A program runs in the current folder (`code .`). Command-line tools run in a terminal that stays open (`ls -la`). Suggestions appear as you type. | `f750488` | 1.0.6 |
| 10 | Group by date, like the Windows Downloads folder, but **only in Downloads** | Added **Group by** (name, date modified, date created, type, size) with collapsible headers: Today, Yesterday, Earlier this week, Last week, and so on. **Downloads opens grouped by date by default**; other folders are not grouped unless you choose it for that folder. | `dc2eded` | 1.0.7 |
| 11 | Home drive icons like Windows | Added **Windows 11-style drive pictures** drawn in code (`Controls/DriveArt.cs`): a grey drive with a green light, a USB stick for removable drives, and your **distribution's logo** (Debian…) or Tux on the system drive. The usage bars are **thick and blue, and turn red** when a drive is almost full. | `8e3d022` | 1.0.8 |
| 12 | "explain me everything about this chat in md file" | This file. | — | — |

**Totals:** 10 commits, 101 files changed, about 15,600 lines added (about 12,800 of them C# and XAML in the Linux app), 11 test files and **60 tests passing**.

---

## 3. How the app is built (architecture)

```text
src/Files.App.Linux
├── App.axaml / Program.cs   // Startup, theme, DI-free service setup
├── Services                 // Logic that does not depend on UI
│   ├── FileSystemService        // List folders, file info
│   ├── FileOperationsService    // Copy/move/delete with progress + cancel
│   ├── TrashService             // freedesktop.org Trash spec (shared with GNOME/KDE)
│   ├── MountService             // Drives from /proc/self/mountinfo, USB hot-plug, eject
│   ├── SettingsService          // ~/.config/files/settings.json
│   ├── FolderViewService        // Per-folder layout/sort/group (Downloads default = group by date)
│   ├── GroupingService          // Today / Yesterday / Last week ... buckets
│   ├── AddressBarService        // Decides: path, file, terminal, program, or shell command
│   ├── LauncherService          // Open files/apps, find terminal, run commands
│   ├── ClipboardService         // System clipboard (gnome-copied-files, uri-list)
│   ├── UndoService              // Undo/redo of file operations
│   ├── ThumbnailService         // Image thumbnails
│   ├── Loc                      // Reads the Windows .resw translations
│   └── Icons/                   // MimeDatabase, IconTheme, IconPack, IconService
├── Models                   // FileItem, SidebarItem, DriveEntryInfo
├── ViewModels               // MainWindow (tabs), ShellTab (one tab), Sidebar, Home, Preview, Properties, Settings, Operations
├── Views                    // MainWindow, ShellView, HomeView, InfoPane, SettingsWindow, PropertiesWindow, dialogs
├── Controls                 // ItemIcon, DetailsRowPanel (resizable columns), DriveArt (drive pictures)
├── Helpers                  // Motion (animation tokens), MenuBuilder, ResourceHelper
└── Styles                   // Controls.axaml, Motion.axaml — Windows-like look
```

**Main libraries:** .NET 10, Avalonia 11.3 (Fluent theme), FluentIcons.Avalonia 2.0, CommunityToolkit.Mvvm 8.4 and Svg.Skia (for SVG icons). Versions are pinned in `Directory.Packages.props`.

**Linux standards it follows**, so it works well with other apps:

- **Trash spec**: shares one trash with GNOME, KDE and XFCE.
- **XDG user dirs**: finds Downloads, Documents and the other user folders in any language.
- **shared-mime-info**: file types.
- **Icon Theme spec**: system icon themes.
- **.desktop files**: "Open with" and the app menu entry.
- **gnome-copied-files clipboard**: copy in Files and paste in Nautilus, and the other way round.
- **Recent files**: shared with GTK apps.

---

## 4. Features

- **Window:** tabs in the title bar; drag tabs to reorder them or out into a new window; multiple windows; session restore.
- **Navigation:** back, forward and up, with history menus; a breadcrumb bar with folder menus; mouse back and forward buttons.
- **Address bar:** type paths, open files or run commands. `cmd` opens a terminal here. Suggestions appear as you type.
- **Sidebar:** Home, pinned folders, Trash and drives (with free space and eject).
- **Home:** Quick access, Windows-style drive cards with usage bars, and recent files.
- **Layouts:** Details (sortable, resizable columns), List, and small, medium or large Grid with thumbnails. **The view is saved for each folder.**
- **Group by:** date, name, type or size. Downloads is grouped by date by default.
- **Files:** open, open with, cut/copy/paste, rename, new file or folder, trash or delete, drag and drop, drag-select, undo/redo, zip/extract, copy path, open in terminal.
- **Trash:** restore, restore all, empty.
- **Search:** recursive, with `*` and `?` wildcards.
- **Details pane:** image and text preview.
- **Properties:** size, dates, owner and editable permissions.
- **Status center:** progress of file operations, with a cancel button.
- **Animations:** WinUI timings; can be turned off.
- **Icons:** Fluent (default), the system theme, any installed theme, or your own icon pack.
- **Settings:** theme, language, startup, default layout, hidden files, extensions, thumbnails, terminal command, and more.

### Keyboard shortcuts (most used)

| Shortcut | Action |
|---|---|
| Ctrl+T / Ctrl+W / Ctrl+Shift+T | New tab / close tab / reopen closed tab |
| Ctrl+N | New window |
| Alt+Left / Alt+Right / Alt+Up | Back / forward / up |
| Ctrl+L, Alt+D, F4 | Edit the address bar |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| F2 | Rename |
| Delete / Shift+Delete | Trash / delete permanently |
| Ctrl+Shift+N | New folder |
| Ctrl+Shift+1…5 | Details / List / Small / Medium / Large |
| Ctrl + mouse wheel | Bigger / smaller items |
| F5 | Refresh |
| Ctrl+Alt+T | Open terminal here |
| Ctrl+, | Settings |

The full list is in `docs/linux.md`.

---

## 5. Custom icons, in short

You can make one **`icons.json`** file, plus image files, that maps file types, extensions and places to icons, for example `"folder": "my-folder.svg"` or `".pdf": "pdf.png"`.

- Import it in **Settings → Appearance → Icons** (a `.zip` works too).
- Optionally **install it as a system icon theme**, so other Linux apps can use the same icons.

The full format is in **`docs/linux-icons.md`**.

---

## 6. How it was tested

- **Unit tests** cover the services: trash, file operations, grouping, address-bar parsing, icon lookup, folder views and drive art.
- **Headless UI tests** (Avalonia.Headless) open real windows in memory, click and type, check the result, and can save screenshots (`FILES_SCREENSHOT_DIR`).
- **Real desktop check**: each feature was also tried on a real X11 desktop (Xvfb + openbox), driven with `xdotool`.
- **Packaging**: every `.deb` from 1.0.0 to 1.0.8 was built and installed with `apt`.

Current result: **60 / 60 tests pass**.

```sh
dotnet run --project tests/Files.App.Linux.Tests
```

---

## 7. Build, install, run

```sh
# Needs the .NET 10 SDK
dotnet build Files.Linux.slnx
dotnet run --project src/Files.App.Linux

# Build the Debian package (amd64 or arm64) -> artifacts/linux/
packaging/linux/build-deb.sh amd64 1.0.8

# Install
sudo apt install ./artifacts/linux/files-app_1.0.8_amd64.deb

# Run
files                # or: files ~/Downloads, or open "Files" from the app menu

# Make it the default file manager (optional)
xdg-mime default files-app.desktop inode/directory

# Remove
sudo apt remove files-app
```

The `.deb` is **self-contained**: no .NET runtime is needed on the target machine. CI (`.github/workflows/ci-linux.yml`) also builds the `.deb` files as downloadable artifacts.

---

## 8. Problems we hit and how they were fixed

**Packaging**

| Problem | Fix |
|---|---|
| The `.deb` wouldn't install: no matching `libicu` version | `libicu` is only recommended now, and the launcher falls back to invariant mode |
| Older dpkg couldn't read zstd packages | Packages use xz compression |

**UI bugs you reported**

| Problem | Fix |
|---|---|
| Toolbar dropdowns were empty | Menus are built when clicked |
| Details columns wouldn't resize | A hidden column's grip was on top of the visible one; cells are now clipped |
| Shortcuts stopped working after clicking a button | Keyboard handling now checks which control has focus |
| Long paths made the address bar hard to click | A clickable strip is always left at the end |

**Development**

| Problem | Fix |
|---|---|
| Animations silently did nothing | Used transform-property setters, which Avalonia supports, and start each animation after layout |

---

## 9. Known limits and not ported yet

- Tabs can be dragged **out** into a new window, but not dropped **into** another window. The new window opens where the drop happened; it doesn't follow the cursor while dragging.
- These Windows features rely on Windows-only APIs and are not ported yet:
  - layouts and panes: dual pane, Columns layout, Cards layout;
  - integrations: file tags, Git integration, FTP and network locations, cloud drive detection;
  - other: the Shelf, the command palette, and inline rename (rename uses a dialog).
- On Wayland the app runs through XWayland.

---

## 10. Where everything is

| What | Where |
|---|---|
| Branch | `claude/compassionate-einstein-3wxykc` |
| Linux app code | `src/Files.App.Linux/` |
| Tests | `tests/Files.App.Linux.Tests/` |
| Packaging | `packaging/linux/` (`build-deb.sh`, launcher `files.sh`, `.desktop`, metainfo, icons) |
| Latest package | `artifacts/linux/files-app_1.0.8_amd64.deb` |
| Main guide | `docs/linux.md` |
| Animations reference | `docs/linux-animations.md` |
| Custom icons guide | `docs/linux-icons.md` |
| README | `.github/README.md` |
| Linux CI | `.github/workflows/ci-linux.yml` |
| Settings file (on your PC) | `~/.config/files/settings.json` |
