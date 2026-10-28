#!/usr/bin/env bash
# Copyright (c) Files Community. Licensed under the MIT License.
#
# Regenerates the animation GIFs used in docs/linux-animations.md.
# Renders the Linux app headlessly frame by frame, then builds GIFs with ImageMagick.
#
# Usage: docs/scripts/make-animation-gifs.sh
# Needs: .NET 10 SDK, ImageMagick (convert). Creates a demo home folder at $FILES_DEMO_HOME (default: a temp dir).

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$ROOT/docs/images/animations"
FRAMES="$(mktemp -d)"
DEMO_HOME="${FILES_DEMO_HOME:-$(mktemp -d)/alex}"
mkdir -p "$DEMO_HOME" "$OUT"

FILES_DEMO_HOME="$DEMO_HOME" FILES_ANIMATION_FRAMES_DIR="$FRAMES" \
	dotnet run --project "$ROOT/tests/Files.App.Linux.Tests" -c Release

for clip in "$FRAMES"/*/; do
	name="$(basename "$clip")"
	# Each frame gets the real time it was on screen (delays.txt, 1/100 s), so the GIF plays at true speed.
	args=()
	i=0
	while read -r delay; do
		args+=(-delay "$delay" "$(printf '%s%03d.png' "$clip" "$i")")
		i=$((i + 1))
	done < "$clip/delays.txt"
	convert "${args[@]}" -resize 640x -layers Optimize -loop 0 "$OUT/$name.gif"
	echo "wrote $OUT/$name.gif"
done
rm -rf "$FRAMES"
