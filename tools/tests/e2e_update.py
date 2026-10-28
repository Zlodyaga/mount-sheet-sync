"""End-to-end: updating the program (and an addon installed from the archive) from GitHub Releases.

A mock GitHub (releases/latest + the asset) serves the real release archive built by tools/package.sh, with a
"new" exe in it. Checks: --check-update, nothing installed without a checksum or with a wrong one, --update
replaces the exe (the old one kept as .old), updates the addon that carries the archive's marker file (keeping
SheetData.lua), leaves an addon without the marker (CurseForge) alone, and says "no updates" for an older tag.

  python3 e2e_update.py <MountSheetSync.exe>
"""
import hashlib
import json
import os
import shutil
import subprocess
import sys
import threading
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import paths

EXE = os.path.abspath(sys.argv[1])
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
W = paths.work("e2e_update")
PORT = 18941
BASE = "http://127.0.0.1:%d" % PORT

checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


shutil.rmtree(W, ignore_errors=True)
os.makedirs(W)

# ---------------------------------------------------------------- the release archive with a "new" exe
NEW_EXE = os.path.join(W, "new", "MountSheetSync.exe")
os.makedirs(os.path.dirname(NEW_EXE))
new_bytes = open(EXE, "rb").read() + b"\0NEW-BUILD-FOR-THE-UPDATE-TEST"
open(NEW_EXE, "wb").write(new_bytes)
DIST = os.path.join(W, "dist")
subprocess.run(["bash", os.path.join(ROOT, "tools", "package.sh"), NEW_EXE, DIST], check=True, capture_output=True)
ZIP = [os.path.join(DIST, f) for f in os.listdir(DIST) if f.endswith(".zip")][0]
zip_bytes = open(ZIP, "rb").read()
names = zipfile.ZipFile(ZIP).namelist()
check("the release archive carries the 'installed from the archive' marker in the addon folder",
      any(n.endswith("/1-addon/MountSheetSync/installed-from-archive.txt") for n in names), names[:5])

STATE = {"tag": "v9.9.9", "digest": "sha256:" + hashlib.sha256(zip_bytes).hexdigest(), "downloads": 0, "list": []}


def release(tag, pre):
    return {"tag_name": tag, "html_url": BASE + "/release/" + tag, "draft": False, "prerelease": pre, "body": "- " + tag,
            "assets": [{"name": os.path.basename(ZIP), "browser_download_url": BASE + "/download/" + os.path.basename(ZIP),
                        "digest": "sha256:" + hashlib.sha256(zip_bytes).hexdigest()}]}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def do_GET(self):
        if self.path == "/repos/Zlodyaga/mount-sheet-sync/releases/latest":
            asset = {"name": os.path.basename(ZIP), "browser_download_url": BASE + "/download/" + os.path.basename(ZIP)}
            if STATE["digest"] is not None:
                asset["digest"] = STATE["digest"]
            body = json.dumps({"tag_name": STATE["tag"], "html_url": BASE + "/release", "draft": False, "prerelease": False,
                               "body": "- Something new\n- And more", "assets": [asset,
                               {"name": "MountSheetSync-addon-only-v9.9.9.zip", "browser_download_url": BASE + "/nope"}]}).encode()
            ok = self.headers.get("User-Agent", "").startswith("MountSheetSync/")
            self.send_response(200 if ok else 403)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(body if ok else b'{"message":"User-Agent required"}')
        elif self.path == "/repos/Zlodyaga/mount-sheet-sync/releases?per_page=30":
            body = json.dumps(STATE["list"]).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(body)
        elif self.path.startswith("/download/"):
            STATE["downloads"] += 1
            self.send_response(302)
            self.send_header("Location", BASE + "/objects/archive.zip")   # like GitHub: a redirect to the storage
            self.end_headers()
        elif self.path == "/objects/archive.zip":
            self.send_response(200)
            self.send_header("Content-Type", "application/octet-stream")
            self.end_headers()
            self.wfile.write(zip_bytes)
        else:
            self.send_response(404)
            self.end_headers()


srv = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
threading.Thread(target=srv.serve_forever, daemon=True).start()


def setup(name, marker):
    """A program folder with a copy of the exe, a data folder and a WoW folder with the addon."""
    d = os.path.join(W, name)
    app = os.path.join(d, "app")
    data = os.path.join(d, "data")
    wow = os.path.join(d, "WoW")
    addon = os.path.join(wow, "_retail_", "Interface", "AddOns", "MountSheetSync")
    os.makedirs(app)
    os.makedirs(data)
    os.makedirs(os.path.join(wow, "_retail_", "WTF", "Account"))
    os.makedirs(addon)
    shutil.copyfile(EXE, os.path.join(app, "MountSheetSync.exe"))
    open(os.path.join(addon, "MountSheetSync.toc"), "w").write("## Interface: 110000\n## Version: 2.0.0\n")
    open(os.path.join(addon, "Old.lua"), "w").write("-- a file the new version no longer has\n")
    open(os.path.join(addon, "SheetData.lua"), "w").write("MountSheetSyncSheet = { remaining = true }\n")
    if marker:
        open(os.path.join(addon, "installed-from-archive.txt"), "w").write("x")
    json.dump({"wowPath": wow}, open(os.path.join(data, "settings.json"), "w"))
    return app, data, addon


def run(app, data, *args, **extra):
    env = dict(os.environ, MSS_UPDATE_TEST=BASE, MSS_SYSTEM_LANG="en", LANG="C.UTF-8", **extra)
    for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
        env.pop(k, None)
    p = subprocess.run(["mono", os.path.join(app, "MountSheetSync.exe"), "--data", data] + list(args), env=env,
                       capture_output=True, timeout=120)
    return p.returncode, (p.stdout + p.stderr).decode("utf-8", "replace")


try:
    old_bytes = open(EXE, "rb").read()
    app, data, addon = setup("manual", True)
    exe = os.path.join(app, "MountSheetSync.exe")

    code, out = run(app, data, "--check-update")
    check("--check-update: the new version and its page", code == 0 and "A new version is available: 9.9.9" in out, out[-600:])
    check("--check-update downloads nothing", STATE["downloads"] == 0)

    STATE["digest"] = None
    code, out = run(app, data, "--update")
    check("no checksum from GitHub: nothing installed", code == 1 and "checksum" in out and open(exe, "rb").read() == old_bytes
          and STATE["downloads"] == 0, out[-600:])

    STATE["digest"] = "sha256:" + "0" * 64
    code, out = run(app, data, "--update")
    check("wrong checksum: refused, the exe and the addon untouched", code == 1 and "does not match" in out
          and open(exe, "rb").read() == old_bytes and os.path.exists(os.path.join(addon, "Old.lua"))
          and not os.path.exists(exe + ".old"), out[-600:])

    STATE["digest"] = "sha256:" + hashlib.sha256(zip_bytes).hexdigest()
    code, out = run(app, data, "--update")
    check("--update exits 0 and logs the new version", code == 0 and "Program updated to 9.9.9" in out, out[-800:])
    check("the exe is the one from the archive; the old one is kept as .old", open(exe, "rb").read() == new_bytes
          and open(exe + ".old", "rb").read() == old_bytes)
    check("no leftovers next to the exe", sorted(os.listdir(app)) == ["MountSheetSync.exe", "MountSheetSync.exe.old"], os.listdir(app))
    toc = open(os.path.join(addon, "MountSheetSync.toc"), encoding="utf-8").read()
    check("the addon installed from the archive is updated", "## Version: 2.0.0" not in toc and "MountSheetSync.lua" in os.listdir(addon)
          and not os.path.exists(os.path.join(addon, "Old.lua")), sorted(os.listdir(addon)))
    check("…keeps its SheetData.lua and the marker", "remaining = true" in open(os.path.join(addon, "SheetData.lua")).read()
          and os.path.exists(os.path.join(addon, "installed-from-archive.txt")))
    iface = os.path.dirname(os.path.dirname(addon))
    check("no temporary folders left in Interface", sorted(os.listdir(iface)) == ["AddOns"], os.listdir(iface))
    check("the log tells to /reload in the game", "/reload" in out, out[-600:])

    # the updated exe starts and cleans the .old up (a tray start would; --check-update is enough to see it run)
    code, out = run(app, data, "--check-update")
    check("the new exe runs", code == 0 and "9.9.9" in out, out[-400:])

    # ---------------------------------------------------------------- CurseForge addon (no marker): left alone
    app2, data2, addon2 = setup("curseforge", False)
    code, out = run(app2, data2, "--update")
    check("addon without the marker (CurseForge) is not touched", code == 0 and os.path.exists(os.path.join(addon2, "Old.lua"))
          and "## Version: 2.0.0" in open(os.path.join(addon2, "MountSheetSync.toc")).read(), out[-600:])
    check("…and the log says CurseForge updates it", "CurseForge updates it" in out, out[-600:])
    check("…the program itself is updated", open(os.path.join(app2, "MountSheetSync.exe"), "rb").read() == new_bytes)

    # ---------------------------------------------------------------- an older release: nothing to do
    STATE["tag"] = "v1.0.0"
    app3, data3, addon3 = setup("older", True)
    n = STATE["downloads"]
    code, out = run(app3, data3, "--update")
    check("older tag: 'no updates', nothing downloaded", code == 0 and "No updates" in out and STATE["downloads"] == n
          and open(os.path.join(app3, "MountSheetSync.exe"), "rb").read() == old_bytes, out[-400:])

    # ---------------------------------------------------------------- test versions (alpha, beta)
    STATE["list"] = [release("v2.10.0-alpha1", True), release("v2.10.0-alpha2", True), release("v1.0.0", False)]
    app4, data4, addon4 = setup("tests", True)
    code, out = run(app4, data4, "--check-update")
    check("pre-releases are not offered by default (the latest stable is older)", code == 0 and "No updates" in out, out[-400:])
    settings4 = json.load(open(os.path.join(data4, "settings.json")))
    settings4["testVersions"] = True
    json.dump(settings4, open(os.path.join(data4, "settings.json"), "w"))
    code, out = run(app4, data4, "--check-update")
    check("with «test versions» on: the newest pre-release (2.10.0-alpha2 > alpha1)", code == 0 and "A new version is available: 2.10.0-alpha2" in out, out[-400:])
    code, out = run(app4, data4, "--update")
    check("…and it installs like a release", code == 0 and "Program updated to 2.10.0-alpha2" in out
          and open(os.path.join(app4, "MountSheetSync.exe"), "rb").read() == new_bytes, out[-600:])
    STATE["list"] = [release("v2.9.0-alpha1", True), release("v2.9.0-beta3", True), release("v1.0.0", False)]
    app5, data5, addon5 = setup("tests-older", True)
    json.dump(dict(json.load(open(os.path.join(data5, "settings.json"))), testVersions=True), open(os.path.join(data5, "settings.json"), "w"))
    code, out = run(app5, data5, "--check-update")
    check("2.9.0-alpha1 / beta3 are older than the 2.9.0 release this exe is", code == 0 and "No updates" in out, out[-400:])
    # an alpha build (tag v2.9.0-alpha1) with test versions off: the 2.9.0 release is newer
    STATE["tag"] = "v2.9.0"
    app6, data6, addon6 = setup("alpha-build", True)
    code, out = run(app6, data6, "--check-update", MSS_TEST_PRERELEASE="alpha1")
    check("an alpha build is offered the 2.9.0 release (same number, no suffix = newer)", code == 0 and "A new version is available: 2.9.0" in out, out[-400:])
    code, out = run(app6, data6, "--check-update")
    check("…while the 2.9.0 release build itself is up to date", code == 0 and "No updates: 2.9.0" in out, out[-400:])
finally:
    srv.shutdown()

failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
