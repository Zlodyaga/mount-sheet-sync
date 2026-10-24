#!/bin/sh
# Builds the test hosts from the tray sources.
set -e
H=$(cd "$(dirname "$0")" && pwd)
cd "$H/../../program"
for host in EngineHost BuilderHost GameHost CollectionHost; do
  mcs -langversion:5 -define:TESTS -target:exe -codepage:65001 -main:MountSheetSync.$host -resource:catalog.json,MountSheetSync.catalog.json -resource:i18n.json,MountSheetSync.i18n.json -resource:sheet-i18n.json,MountSheetSync.sheet-i18n.json \
    -nowarn:649 -out:$H/$host.exe -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Security.dll -r:System.IO.Compression.dll \
    *.cs $H/EngineHost.cs $H/BuilderHost.cs $H/GameHost.cs $H/CollectionHost.cs | grep -v "^Compilation succeeded" || true
done
