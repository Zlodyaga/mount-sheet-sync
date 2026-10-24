"""The tray program finds a new version on (a mock) GitHub, offers it in the icon menu, installs it on a click and
restarts as the new version. Runs under mono + Xvfb; screenshots go to the run folder.

  python3 ui_update.py <MountSheetSync.exe>
"""
import hashlib
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import paths

EXE = os.path.abspath(sys.argv[1])
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
W = paths.work("ui_update")
DISPLAY = ":95"
PORT = 18943
BASE = "http://127.0.0.1:%d" % PORT
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1200]))


shutil.rmtree(W, ignore_errors=True)
os.makedirs(W)

# the release: the same program with a few extra bytes, packed by tools/package.sh
new_exe = os.path.join(W, "new", "MountSheetSync.exe")
os.makedirs(os.path.dirname(new_exe))
new_bytes = open(EXE, "rb").read() + b"\0NEW-BUILD-FOR-THE-UI-UPDATE-TEST"
open(new_exe, "wb").write(new_bytes)
dist = os.path.join(W, "dist")
subprocess.run(["bash", os.path.join(ROOT, "tools", "package.sh"), new_exe, dist], check=True, capture_output=True)
zip_path = [os.path.join(dist, f) for f in os.listdir(dist) if f.endswith(".zip")][0]
zip_bytes = open(zip_path, "rb").read()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def do_GET(self):
        if self.path == "/repos/Zlodyaga/mount-sheet-sync/releases/latest":
            body = json.dumps({"tag_name": "v9.9.9", "html_url": BASE + "/release", "body": "- New things\n- More new things",
                               "assets": [{"name": os.path.basename(zip_path), "browser_download_url": BASE + "/archive.zip",
                                           "digest": "sha256:" + hashlib.sha256(zip_bytes).hexdigest()}]}).encode()
        elif self.path == "/archive.zip":
            body = zip_bytes
        else:
            self.send_response(404)
            self.end_headers()
            return
        self.send_response(200)
        self.end_headers()
        self.wfile.write(body)


srv = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
threading.Thread(target=srv.serve_forever, daemon=True).start()

app = os.path.join(W, "app")
data = os.path.join(W, "data")
wow = os.path.join(W, "WoW")
addon = os.path.join(wow, "_retail_", "Interface", "AddOns", "MountSheetSync")
for d in (app, data, os.path.join(wow, "_retail_", "WTF", "Account"), addon):
    os.makedirs(d)
open(os.path.join(addon, "MountSheetSync.toc"), "w").write("## Version: 2.0.0\n")
open(os.path.join(addon, "installed-from-archive.txt"), "w").write("x")
app_exe = os.path.join(app, "MountSheetSync.exe")
shutil.copyfile(EXE, app_exe)
json.dump({"wowPath": wow, "pollSeconds": 2}, open(os.path.join(data, "settings.json"), "w"))

env = dict(os.environ, DISPLAY=DISPLAY, LANG="C.UTF-8", MSS_SYSTEM_LANG="en", MSS_UPDATE_TEST=BASE, MSS_UPDATE_DELAY="2",
           MONO_REGISTRY_PATH=os.path.join(W, "registry"))
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
    env.pop(k, None)
procs = [subprocess.Popen(["Xvfb", DISPLAY, "-screen", "0", "1280x900x24"], stderr=subprocess.DEVNULL)]
time.sleep(1)
procs.append(subprocess.Popen(["stalonetray", "--geometry", "4x1+1100+0", "--icon-size", "24"], env=env,
                              stderr=subprocess.DEVNULL, stdout=subprocess.DEVNULL))
time.sleep(1)


def xdo(*args):
    return subprocess.run(["xdotool"] + list(args), env=env, capture_output=True, text=True).stdout


def shot(name):
    subprocess.run(["import", "-window", "root", os.path.join(W, name + ".png")], env=env, check=False)


def log_text():
    p = os.path.join(data, "sync.log")
    return open(p, encoding="utf-8-sig").read() if os.path.exists(p) else ""


def wait_log(text, timeout=40):
    end = time.time() + timeout
    while time.time() < end:
        if text in log_text():
            return True
        time.sleep(0.5)
    return False


def window(part):
    for wid in xdo("search", "--name", part).split():
        return wid
    return None


try:
    # --autostart: no settings window over the menu (the program is not set up)
    tray = subprocess.Popen(["mono", app_exe, "--data", data, "--autostart"], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    check("the tray program finds the new version on its own", wait_log("A new version is available: 9.9.9"), log_text()[-600:])
    time.sleep(1.5)
    shot("v1_notice")
    st = json.load(open(os.path.join(data, "settings.json"), encoding="utf-8-sig"))
    check("it reminds about this version once (remembered in the settings)", st.get("updateNotified") == "9.9.9"
          and st.get("checkUpdates") is True, st)
    xdo("mousemove", "1112", "12", "click", "3")
    time.sleep(1.5)
    shot("v2_menu")
    xdo("mousemove", "900", "48", "click", "1")   # the second line: «Update to version 9.9.9…»
    end = time.time() + 20
    box = None
    while time.time() < end and box is None:
        box = window("Mount Sheet Sync")
        time.sleep(0.4)
    check("«Update to version 9.9.9…» asks first", box is not None)
    time.sleep(1)
    shot("v3_confirm")
    xdo("windowfocus", "--sync", box)
    xdo("key", "--window", box, "Return")   # OK
    check("installed and restarted", wait_log("Program updated to 9.9.9") and wait_log("Update installed:", 60), log_text()[-900:])
    check("the exe in the program folder is the new one", open(app_exe, "rb").read() == new_bytes)
    check("the addon from the archive was updated", "## Version: 2.0.0" not in open(os.path.join(addon, "MountSheetSync.toc")).read())
    check("the old process has exited", wait_exit := (tray.wait(timeout=40) is not None))
    time.sleep(3)
    running = subprocess.run(["pgrep", "-f", "MountSheetSync.exe --wait-pid"], capture_output=True, text=True).stdout.split()
    check("the new version runs in the tray", len(running) == 1, running)
    check("the new version removed MountSheetSync.exe.old", not os.path.exists(app_exe + ".old"), os.listdir(app))
    shot("v4_after_restart")
finally:
    subprocess.run(["pkill", "-f", app_exe], capture_output=True)
    for p in procs:
        p.kill()
    srv.shutdown()

failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
