# Files for Linux

`src/Files.App.Linux` is a Linux port of Files. The Windows app is built on WinUI 3, which only runs on Windows, so the Linux port uses [Avalonia](https://avaloniaui.net/), a cross-platform UI framework with the same C#/XAML model. The layout, colors, icons (Fluent UI System Icons) and translations follow the Windows app.

It targets Debian 12 and 13 and should also work on Ubuntu and other Debian-based distributions. It runs on X11, and on Wayland through XWayland.

## Install on Debian

Download `files-app_<version>_amd64.deb` (or `arm64`) from the **Files Linux CI** workflow artifacts, or build it yourself (see below), then:

```sh
sudo apt install ./files-app_1.0.0_amd64.deb
```

Start it from the app menu (**Files**) or run `files` (optionally with a folder: `files ~/Downloads`).

To make Files the default file manager (for "Open folder" actions in other apps):

```sh
xdg-mime default files-app.desktop inode/directory
```

The package is self-contained: it does not need a .NET runtime on the machine. ICU (`libicu*`) is only recommended; if no version is installed, the `files` launcher starts the app in invariant globalization mode (translations still follow `LANG`, dates use a neutral format). Uninstall with `sudo apt remove files-app`.

## Build from source

You need the .NET 10 SDK. On Debian, install it from Microsoft's package repository (see [Install .NET on Debian](https://learn.microsoft.com/dotnet/core/install/linux-debian)); on Ubuntu 24.04+ `sudo apt install dotnet-sdk-10.0` works.

```sh
# Run from source
dotnet run --project src/Files.App.Linux

# Run the tests (unit tests + headless UI tests)
dotnet run --project tests/Files.App.Linux.Tests

# Build a .deb (amd64 or arm64) into artifacts/linux/
packaging/linux/build-deb.sh amd64 1.0.0
```

Set `FILES_SCREENSHOT_DIR=/some/folder` when running the tests to save screenshots of the main screens.

## Features

| Area | What works |
| --- | --- |
| Window | Drag tabs to reorder them, or drag a tab out of the tab bar to open it in its own window (it keeps its back/forward history). Tabs in the title bar with Windows-style minimize/maximize/close buttons (drag the empty area to move, double-click to maximize; Settings → Appearance → *Use the system title bar* switches back to the desktop's title bar), tabs (new, close, duplicate, reopen closed, close others/to the right, middle-click to open in a new tab), multiple windows, session restore |
| Navigation | Back/forward (with history menus), up, refresh, breadcrumb bar with sub-folder menus, mouse back/forward buttons |
| Address bar | Click an empty part (or the folder icon, Ctrl+L, Alt+D, F4) to type. Like Windows: a path goes there (`~`, `..` and relative paths work), a file opens, **`cmd`** / `terminal` / `powershell` / `wt` opens a terminal in the current folder, a program runs in the current folder (`code .`, `gedit notes.txt`), and a command-line tool runs in a terminal that stays open (`ls -la`, `htop`, `./build.sh`). Suggestions appear while typing (Up/Down + Enter, Tab to fill in). Right-click: Edit address, Copy address, Open in Terminal |
| Sidebar | Home, pinned folders (pin/unpin/reorder), Trash, drives with free space (auto-updates when USB drives are plugged in), eject removable drives, collapsible sections, drag and drop onto items |
| Home page | Quick access, drives with usage bars, recent files (shared with GTK apps) |
| Layouts | Details (sortable, resizable columns), List, Grid (small/medium/large) with image thumbnails. **Each folder remembers its own layout and sort**: a folder opens with the default from Settings until you change it there, and the change applies to that folder only (Layout menu → *Use the default view for this folder* undoes it; Settings → *Reset folder views* resets all). Ctrl + mouse wheel changes the item size |
| Files | Open, open with (installed apps from `.desktop` files), cut/copy/paste (also published to the system clipboard in the `x-special/gnome-copied-files` and `text/uri-list` formats other file managers use), rename, new folder/file, delete to trash or permanently, drag and drop (move within a drive, copy across drives, Ctrl to copy; the target folder is highlighted with a "Move to …" label), drag-select box (press on empty space and drag), undo/redo for rename, new, copy, move and delete to trash, compress to ZIP, extract ZIP/TAR/TAR.GZ, copy path, open in terminal |
| Trash | freedesktop.org Trash spec (same trash as GNOME/KDE/XFCE, including trash folders on other drives), restore, restore all, empty |
| Search | Recursive search from the current folder, with `*`/`?` wildcards |
| Details pane | Preview of images and text files, file details |
| Properties | Size (with folder contents count), dates, symlink target, owner/group, editable permissions |
| Status center | Progress for copy/move/delete/compress/extract, with cancel |
| Animations | Windows (WinUI) motion: folder contents and Home widgets rise in, the sidebar slides, dialogs scale in, menus slide down, hover colors fade. Same timings and curves as Windows; can be turned off. See [Animations reference](linux-animations.md) |
| Icons | Files' Fluent icons, your desktop's icon theme, any installed icon theme (Papirus, Adwaita…), or your own icon pack (one `icons.json` file). A pack can be imported from a `.zip` and installed as a system icon theme for all apps. See [Custom icons](linux-icons.md) |
| Settings | Theme (system/light/dark), animations on/off, language (all Windows translations), startup behavior, default layout, hidden files, file extensions, thumbnails, delete confirmation, folders first, terminal command |

### Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+T / Ctrl+W / Ctrl+Shift+T | New tab / close tab / reopen closed tab |
| Ctrl+Tab, Ctrl+Shift+Tab, Ctrl+1…9 | Switch tabs |
| Ctrl+N | New window |
| Alt+Left / Alt+Right / Alt+Up / Backspace | Back / forward / up / back |
| Ctrl+L, Alt+D, F4 | Edit the path |
| Ctrl+F, Ctrl+E, F3 | Search |
| F5, Ctrl+R | Refresh |
| Enter / Ctrl+Enter / Alt+Enter | Open / open in new tab / properties |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste |
| Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) | Undo / redo |
| Ctrl+D | Move to trash |
| Ctrl + mouse wheel | Bigger / smaller items |
| Ctrl+Shift+C | Copy path |
| F2 | Rename |
| Delete / Shift+Delete | Move to trash / delete permanently |
| Ctrl+Shift+N | New folder |
| Ctrl+A / Esc | Select all / clear selection |
| Ctrl+H | Show hidden files |
| Ctrl+Shift+1…5 | Details / List / Small / Medium / Large icons |
| Ctrl+Shift+B / Alt+P | Toggle sidebar / details pane |
| Ctrl+Alt+T | Open in terminal |
| Ctrl+, | Settings |
| Typing letters | Jump to the first matching item |

### Not ported yet

These Windows features depend on Windows-only APIs and are not in the Linux port yet: dual pane, Columns layout, Cards layout, file tags, Git integration, FTP and network locations, cloud drive detection, the Shelf, the command palette, and inline rename (rename uses a dialog).

## Code layout

```text
src/Files.App.Linux
├── Services     // File system, file operations, freedesktop trash, mounts/drives, settings, clipboard, launcher, localization
├── Models       // FileItem, SidebarItem
├── ViewModels   // MainWindow (tabs), ShellTab (navigation, items, commands), Sidebar, Home, Preview, Properties, Settings
├── Views        // MainWindow, ShellView (toolbar + layouts + status bar), Home, details pane, dialogs, Settings, Properties
├── Controls     // ItemIcon (folder art, file icons, thumbnails), DetailsRowPanel (shared column widths)
└── Styles       // Control styles matching the Windows app
packaging/linux  // .deb build script, .desktop entry, AppStream metadata, icons
tests/Files.App.Linux.Tests // MSTest unit tests and Avalonia headless UI tests
```

Strings come from `src/Files.App/Strings/<language>/Resources.resw`, the same files the Windows app uses, so translations stay in one place.

Settings are stored in `~/.config/files/settings.json`.
