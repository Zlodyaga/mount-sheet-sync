#!/usr/bin/env bash
# Puts together the full release archive MountSheetSync-<version>.zip:
#   README.txt (Russian guide, BOM + CRLF), 1-addon/MountSheetSync (CRLF), 2-program/MountSheetSync.exe
#   + source (BOM + CRLF), 3-old-way-script/Code.gs.txt.
#   tools/package.sh <MountSheetSync.exe> [out-dir]
# The exe comes from program/build.cmd on Windows (CI) or tools/build_exe.ps1 on Linux.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
EXE=$(realpath "$1")
OUTDIR=$(realpath -m "${2:-$ROOT/dist}")
VERSION=$(sed -n 's/^## Version: *//p' "$ROOT/MountSheetSync.toc" | tr -d '\r')
mkdir -p "$OUTDIR"
python3 - "$ROOT" "$EXE" "$OUTDIR" "$VERSION" <<'PY'
import os, shutil, sys, tempfile, time, zipfile
root, exe, outdir, version = sys.argv[1:5]
short = ".".join(version.split(".")[:2])
name = "MountSheetSync-" + short
st = tempfile.mkdtemp()
r = os.path.join(st, name)
addon = os.path.join(r, "1-addon", "MountSheetSync")
src = os.path.join(r, "2-program", "source")
os.makedirs(addon); os.makedirs(src); os.makedirs(os.path.join(r, "3-old-way-script"))

def crlf(text):
    return text.replace("\r\n", "\n").replace("\n", "\r\n")

def put(dst, text, bom):
    open(dst, "wb").write((b"\xef\xbb\xbf" if bom else b"") + crlf(text).encode("utf-8"))

def read(p):
    return open(p, encoding="utf-8-sig").read()

put(os.path.join(r, "README.txt"), read(os.path.join(root, "docs", "README.ru.txt")), True)
put(os.path.join(r, "3-old-way-script", "Code.gs.txt"), read(os.path.join(root, "script", "Code.gs")), True)
# the addon: what the .toc loads, plus Libs (with the Ace3 license)
toc = read(os.path.join(root, "MountSheetSync.toc"))
put(os.path.join(addon, "MountSheetSync.toc"), toc, False)
for line in toc.splitlines():
    line = line.strip()
    if line and not line.startswith("#"):
        rel = line.replace("\\", "/")
        dst = os.path.join(addon, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        put(dst, read(os.path.join(root, rel)), False)
shutil.copyfile(os.path.join(root, "Libs", "CallbackHandler-1.0", "LICENSE.txt"),
                os.path.join(addon, "Libs", "CallbackHandler-1.0", "LICENSE.txt"))
assert "MountSheetSyncSheet = nil" in read(os.path.join(addon, "SheetData.lua")), "SheetData.lua must ship empty"
prog = os.path.join(root, "program")
for f in sorted(os.listdir(prog)):
    p = os.path.join(prog, f)
    if f.endswith(".cs"):
        put(os.path.join(src, f), read(p), True)
    elif f in ("build.cmd", "app.manifest"):
        put(os.path.join(src, f), read(p), False)
    elif f.endswith(".ico") or f in ("catalog.json", "i18n.json"):
        shutil.copyfile(p, os.path.join(src, f))
    elif f != "MountSheetSync.exe":
        raise SystemExit("unexpected file in program/: " + f)
shutil.copyfile(exe, os.path.join(r, "2-program", "MountSheetSync.exe"))
out = os.path.join(outdir, name + ".zip")
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for dirpath, dirnames, filenames in os.walk(r):
        dirnames.sort()
        for f in sorted(filenames):
            p = os.path.join(dirpath, f)
            arc = os.path.relpath(p, st).replace(os.sep, "/")
            info = zipfile.ZipInfo(arc, date_time=time.localtime(os.path.getmtime(p))[:6])
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            z.writestr(info, open(p, "rb").read(), compresslevel=9)
shutil.rmtree(st)
z = zipfile.ZipFile(out)
print(out, os.path.getsize(out), "bytes,", len(z.namelist()), "files, test:", z.testzip() or "OK")
PY
