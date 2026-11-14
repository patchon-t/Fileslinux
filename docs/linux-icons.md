# Custom icons (Linux port)

Files can show three kinds of icons for files, folders, places (Home, Desktop, Downloads…, Trash) and drives. Pick one in **Settings → Appearance → Icons**:

| Option | What you get |
| --- | --- |
| **Files (Fluent)** | Files' own Windows 11–style icons (default). |
| **System icon theme** | The icon theme your desktop uses, so Files matches your other apps. It follows GNOME, Cinnamon, MATE, Budgie, KDE and XFCE settings. |
| **Theme: …** | Any installed freedesktop.org icon theme (Papirus, Adwaita, Breeze, Numix…), found in `~/.local/share/icons`, `~/.icons` and `/usr/share/icons`. |
| **Icon pack: …** | Your own icons, defined by one file: `icons.json`. |

Toolbar and menu icons stay in the Fluent style, like on Windows.

## Make an icon pack

The quickest start: **Settings → Appearance → Icons → Create example pack**. It creates a working pack, switches to it and opens its folder. Edit the files, then press **Reload**.

A pack is a folder with an `icons.json` file and the images it points to (PNG or SVG; SVG stays sharp at every size):

```text
~/.local/share/files/icon-packs/my-pack/
├── icons.json
└── icons/
    ├── folder.svg
    ├── file.svg
    ├── pdf.svg
    └── …
```

```json
{
  "name": "My pack",
  "author": "You",
  "description": "Purple folders",
  "fallback": "system",
  "icons": {
    "folder": "icons/folder.svg",
    "file": "icons/file.svg",
    ".pdf": "icons/pdf.svg",
    ".zip": "icons/archive.svg",
    "kind/image": "icons/image.svg",
    "mime/text/markdown": "icons/markdown.svg",
    "places/home": "icons/home.svg",
    "places/trash": "icons/trash.svg",
    "places/drive": "icons/drive.svg"
  }
}
```

### Keys

When Files needs an icon, it uses the first key the pack defines, in this order.

**Files:**

1. `.ext`: the file extension, for example `.pdf`, `.docx`, `.tar.gz`.
2. `mime/TYPE`: the MIME type, for example `mime/application/pdf` or `mime/text/markdown`.
3. `kind/KIND`: one of `image`, `video`, `audio`, `archive`, `document`, `spreadsheet`, `presentation`, `pdf`, `text`, `code`, `executable`, `font`, `diskimage`, `package`.
4. `file`: any other file.

**Folders:** `folder`.

**Places:** `places/home`, `places/desktop`, `places/downloads`, `places/documents`, `places/pictures`, `places/music`, `places/videos`, `places/trash`, `places/trash-full`, `places/drive`, `places/drive-removable`. Folder places fall back to `folder`.

### Fallback

`fallback` decides where icons the pack doesn't define come from:

- `"builtin"` (default): Files' own icons.
- `"system"`: your desktop's icon theme.
- `"theme:NAME"`: a specific installed theme, for example `"theme:Papirus"`.

Image paths must stay inside the pack folder; paths that point outside it are ignored.

## Share a pack

Zip the pack folder and send the `.zip`. To install one: **Settings → Appearance → Icons → Import icon pack…**, then choose the `.zip` (or a pack's `icons.json`). It is copied to `~/.local/share/files/icon-packs/` and selected.

## Use a pack in all your apps

Select the pack, then press **Use for all apps**. Files then:

1. **Installs the pack as a system icon theme.** It writes a freedesktop.org icon theme named `Files-<pack>` in `~/.local/share/icons/`, with your images under the standard icon names (`folder`, `user-home`, `application-pdf`, `image-x-generic`, …). The theme inherits from your current theme, so anything the pack doesn't define looks as before.
2. **Switches your desktop to it**, where it can:
   - GNOME, Cinnamon, MATE, Budgie: through `gsettings`.
   - KDE Plasma: through `plasma-changeicons`.
   - XFCE: through `xfconf-query`.

   Otherwise, choose the `Files-<pack>` theme in your desktop's appearance settings.

To go back, pick your previous icon theme in the desktop's appearance settings.
