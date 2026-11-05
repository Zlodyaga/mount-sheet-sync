"""UI walk-through of a new player under mono + Xvfb: no settings at all, the settings window opens once with nothing to sign in to,
«Сохранить» keeps the data on this computer, the data makes itself once the addon knows a character, and the next start stays quiet.

  python3 ui_local.py <MountSheetSync.exe>
"""
import json
import os
import shutil
import subprocess
import sys
import time
import paths

EXE = os.path.abspath(sys.argv[1])
W = paths.work("ui_local")
DISPLAY = ":95"
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:900]))


shutil.rmtree(W, ignore_errors=True)
DATA = os.path.join(W, "data")
WOW = os.path.join(W, "WoW")
SV_DIR = os.path.join(WOW, "_retail_", "WTF", "Account", "ACC1", "SavedVariables")
ADDON = os.path.join(WOW, "_retail_", "Interface", "AddOns", "MountSheetSync")
for d in (DATA, SV_DIR, ADDON):
    os.makedirs(d)
open(os.path.join(ADDON, "MountSheetSync.toc"), "w").write("## Interface: 120105\n")
SETTINGS = os.path.join(DATA, "settings.json")
env = dict(os.environ, DISPLAY=DISPLAY, LANG="ru_RU.UTF-8", MSS_SYSTEM_LANG="ru", MONO_REGISTRY_PATH=os.path.join(W, "registry"),
           MSS_BROWSER_LOG=os.path.join(W, "browser.txt"), MSS_GOOGLE_TEST="http://127.0.0.1:18996")
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY"):
    env.pop(k, None)
procs = [subprocess.Popen(["Xvfb", DISPLAY, "-screen", "0", "1280x900x24"], stderr=subprocess.DEVNULL)]
time.sleep(1)
procs.append(subprocess.Popen(["stalonetray", "--geometry", "4x1+1100+0"], env=env, stderr=subprocess.DEVNULL, stdout=subprocess.DEVNULL))
time.sleep(1)


def shot(name):
    subprocess.run(["import", "-window", "root", os.path.join(W, name + ".png")], env=env, check=False)


def windows():
    p = subprocess.run(["xdotool", "search", "--name", "Mount Sheet Sync"], env=env, capture_output=True, text=True)
    return [(wid, subprocess.run(["xdotool", "getwindowname", wid], env=env, capture_output=True, text=True).stdout.strip()) for wid in p.stdout.split()]


def wait_window(part, timeout=40):
    end = time.time() + timeout
    while time.time() < end:
        for wid, n in windows():
            if part in n:
                return wid
        time.sleep(0.4)
    return None


def key(wid, k):
    subprocess.run(["xdotool", "windowfocus", "--sync", wid], env=env, capture_output=True)
    time.sleep(0.3)
    subprocess.run(["xdotool", "key", "--window", wid, k], env=env)


def settings():
    return json.load(open(SETTINGS, encoding="utf-8-sig"))


def wait_file(path, timeout=60):
    end = time.time() + timeout
    while time.time() < end:
        if os.path.exists(path):
            return True
        time.sleep(0.5)
    return False


def write_sv():
    open(os.path.join(SV_DIR, "MountSheetSync.lua"), "w", encoding="utf-8", newline="\r\n").write(
        'MountSheetSyncDB = {\n\t["chars"] = {\n\t\t["Эйра-Ревущий фьорд"] = {\n\t\t\t["name"] = "Эйра",\n\t\t\t["realm"] = "Ревущий фьорд",\n'
        '\t\t\t["classFile"] = "HUNTER",\n\t\t\t["level"] = 90,\n\t\t\t["seen"] = %d,\n\t\t},\n\t},\n\t["kills"] = {\n\t},\n}\n' % (int(time.time()) - 100))


def start(*args):
    return subprocess.Popen(["mono", EXE, "--data", DATA] + list(args), env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)


def stop(app):
    app.terminate()
    try:
        return app.communicate(timeout=10)[0].decode("utf-8", "replace")
    except Exception:
        app.kill()
        return ""


app = None
try:
    json.dump({"wowPath": WOW, "pollSeconds": 2}, open(SETTINGS, "w"))
    app = start()
    wid = wait_window("настройки")
    check("a new player: the settings window opens", wid is not None, windows())
    time.sleep(1.5)
    shot("l1_first_run")
    check("…only once: it is remembered right away", settings().get("welcomed") is True, settings())
    check("…the data doesn't exist yet", not os.path.exists(os.path.join(DATA, "local-data.json")))
    key(wid, "Return")
    end = time.time() + 15
    while time.time() < end and wait_window("настройки", 1) is not None:
        time.sleep(0.5)
    check("«Сохранить» closes the window, no sign-in window instead", wait_window("настройки", 1) is None and not [n for _, n in windows() if "вход" in n.lower()], windows())
    check("…the data stays on this computer", settings().get("mode") == "local", settings())
    check("…no browser was opened for Google", not os.path.exists(env["MSS_BROWSER_LOG"]))
    write_sv()
    made = wait_file(os.path.join(DATA, "local-data.json"))
    check("the addon knows a character: the data makes itself, the program keeps running", made and app.poll() is None, os.listdir(DATA))
    check("…the list is written for the addon", wait_file(os.path.join(ADDON, "SheetData.lua")))
    stop(app)
    time.sleep(1)
    app = start()
    time.sleep(6)
    check("the next start opens no window", wait_window("настройки", 3) is None and app.poll() is None, windows())
    shot("l2_second_start")
    stop(app)
    app = None
finally:
    if app is not None:
        stop(app)
    for p in reversed(procs):
        p.terminate()

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
