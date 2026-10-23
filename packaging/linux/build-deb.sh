#!/usr/bin/env bash
# Copyright (c) Files Community. Licensed under the MIT License.
#
# Builds a Debian package (.deb) for the Linux port of Files.
#
# Usage: packaging/linux/build-deb.sh [amd64|arm64] [version]
#   Output: artifacts/linux/files-app_<version>_<arch>.deb
#
# The app is published self-contained, so the package does not need a .NET runtime on the target machine.

set -euo pipefail

ARCH="${1:-amd64}"
VERSION="${2:-1.0.0}"

case "$ARCH" in
	amd64) RID="linux-x64" ;;
	arm64) RID="linux-arm64" ;;
	*) echo "Unsupported architecture: $ARCH (use amd64 or arm64)" >&2; exit 1 ;;
esac

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HERE="$ROOT/packaging/linux"
OUT="$ROOT/artifacts/linux"
STAGE="$OUT/stage-$ARCH"
PKG="files-app"
INSTALL_DIR="/usr/lib/$PKG"

rm -rf "$STAGE"
mkdir -p "$STAGE/DEBIAN" "$STAGE$INSTALL_DIR" "$STAGE/usr/bin" "$STAGE/usr/share/applications" "$STAGE/usr/share/metainfo" "$STAGE/usr/share/doc/$PKG"

echo "==> Publishing Files ($RID, $VERSION)"
dotnet publish "$ROOT/src/Files.App.Linux/Files.App.Linux.csproj" \
	-c Release \
	-r "$RID" \
	--self-contained true \
	-p:Version="$VERSION" \
	-p:DebugType=none \
	-o "$STAGE$INSTALL_DIR"

ln -s "$INSTALL_DIR/files" "$STAGE/usr/bin/files"
install -m 644 "$HERE/files-app.desktop" "$STAGE/usr/share/applications/files-app.desktop"
install -m 644 "$HERE/files-app.metainfo.xml" "$STAGE/usr/share/metainfo/files-app.metainfo.xml"
for size in 32 48 64 128 256; do
	dir="$STAGE/usr/share/icons/hicolor/${size}x${size}/apps"
	mkdir -p "$dir"
	install -m 644 "$HERE/icons/files-app-$size.png" "$dir/files-app.png"
done
install -m 644 "$ROOT/LICENSE" "$STAGE/usr/share/doc/$PKG/copyright"

# Normalize permissions (dpkg-deb rejects group/world-writable files).
find "$STAGE" -type d -exec chmod 755 {} +
find "$STAGE$INSTALL_DIR" -type f -exec chmod 644 {} +
chmod 755 "$STAGE$INSTALL_DIR/files"
find "$STAGE$INSTALL_DIR" -name "*.so" -exec chmod 755 {} +

INSTALLED_SIZE="$(du -sk "$STAGE" --exclude=DEBIAN | cut -f1)"

cat > "$STAGE/DEBIAN/control" <<CONTROL
Package: $PKG
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Installed-Size: $INSTALLED_SIZE
Maintainer: Files Community <hello@files.community>
Homepage: https://files.community
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libfontconfig1, libx11-6, libice6, libsm6, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67 | libicu63, xdg-utils
Recommends: x-terminal-emulator, gvfs
Description: Modern file manager
 Files is a modern file manager with tabs, a sidebar with pinned folders
 and drives, details/list/grid layouts, a details and preview pane,
 recursive search, compress/extract and freedesktop.org trash support.
 This is the Linux port of the Files app for Windows.
CONTROL

cat > "$STAGE/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
	update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
	gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi
POSTINST
cp "$STAGE/DEBIAN/postinst" "$STAGE/DEBIAN/postrm"
chmod 755 "$STAGE/DEBIAN/postinst" "$STAGE/DEBIAN/postrm"

DEB="$OUT/${PKG}_${VERSION}_${ARCH}.deb"
echo "==> Building $DEB"
dpkg-deb --root-owner-group -Zxz --build "$STAGE" "$DEB"
rm -rf "$STAGE"
echo "==> Done: $DEB"
