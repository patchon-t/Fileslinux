#!/bin/sh
# Launcher for Files. .NET needs the ICU library for culture-aware formatting; if no version of
# libicu is installed, run in invariant globalization mode instead of failing to start.
if [ -z "$DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" ] && ! ldconfig -p 2>/dev/null | grep -q 'libicuuc\.so\.[0-9]'; then
	export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi
exec /usr/lib/files-app/files "$@"
