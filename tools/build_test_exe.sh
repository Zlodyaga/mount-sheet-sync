#!/bin/sh
# Quick mono build of the tray program for tests (the release build is tools/build_exe.ps1).
set -e
OUT=$(realpath -m "${1:-${MSS_TEST_TMP:-${TMPDIR:-/tmp}/mss-tests}/mss.exe}")
mkdir -p "$(dirname "$OUT")"
cd "$(dirname "$0")/../program"
mcs -langversion:5 -define:TESTS -target:winexe -codepage:65001 -nowarn:649 \
  -resource:catalog.json,MountSheetSync.catalog.json -resource:i18n.json,MountSheetSync.i18n.json -resource:app-small.ico,MountSheetSync.app.ico -resource:warn-small.ico,MountSheetSync.warn.ico \
  -out:$OUT -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Security.dll *.cs
