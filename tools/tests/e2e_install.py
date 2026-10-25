"""End-to-end: the program installs itself (MountSheetSync.exe downloaded on its own), under mono.

--install from the Downloads folder copies the exe to the install folder and registers it in "Installed apps"
(HKCU Uninstall key, in mono's file registry); a newer download replaces the installed copy; --uninstall --yes
removes the key and the install folder and keeps the settings. Then, under Xvfb, the first start from Downloads
asks: "Yes" installs and starts the installed copy, "Cancel" does nothing, "No" runs from Downloads and is not
asked again.

  python3 e2e_install.py <MountSheetSync.exe>
"""
import glob
import json
import os
import shutil
import subprocess
import sys
import time
import paths

EXE = os.path.abspath(sys.argv[1])
W = paths.work("e2e_install")
DISPLAY = ":94"
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


shutil.rmtree(W, ignore_errors=True)
DOWN = os.path.join(W, "Downloads")
INST = os.path.join(W, "LocalAppData", "Programs", "MountSheetSync")
DATA = os.path.join(W, "data")
HOME = os.path.join(W, "home")   # mono keeps HKCU in files under ~/.mono/registry (MONO_REGISTRY_PATH is not used for it)
REG = os.path.join(HOME, ".mono", "registry")
for d in (DOWN, DATA, HOME):
    os.makedirs(d)
dl_exe = os.path.join(DOWN, "MountSheetSync.exe")
shutil.copyfile(EXE, dl_exe)
inst_exe = os.path.join(INST, "MountSheetSync.exe")

env = dict(os.environ, MSS_INSTALL_DIR=INST, MSS_DOWNLOADS_DIR=DOWN, MSS_SYSTEM_LANG="en", LANG="C.UTF-8",
           HOME=HOME, DISPLAY=DISPLAY)
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
    env.pop(k, None)


def run(exe, *args):
    p = subprocess.run(["mono", exe, "--data", DATA] + list(args), env=env, capture_output=True, timeout=120)
    return p.returncode, (p.stdout + p.stderr).decode("utf-8", "replace")


def uninstall_values():
    """mono keeps HKCU in files: …/HKEY_CURRENT_USER/software/…/uninstall/mountsheetsync/values.xml"""
    for f in glob.glob(os.path.join(REG, "**", "values.xml"), recursive=True):
        if os.path.basename(os.path.dirname(f)).lower() == "mountsheetsync" and "uninstall" in f.lower():
            return open(f, encoding="utf-8", errors="replace").read()
    return None


code, out = run(dl_exe, "--install")
check("--install exits 0 and says where", code == 0 and "Program installed: " + inst_exe in out, out[-600:])
check("the installed exe is a copy of the downloaded one", os.path.exists(inst_exe) and open(inst_exe, "rb").read() == open(EXE, "rb").read())
vals = uninstall_values()
check("registered in Installed apps: name, version, uninstall command", vals is not None and "Mount Sheet Sync" in vals
      and "--uninstall" in vals and inst_exe in vals and "2.8.0" in vals, vals)

newer = open(EXE, "rb").read() + b"\0A-NEWER-DOWNLOAD"
open(dl_exe, "wb").write(newer)
code, out = run(dl_exe, "--install")
check("a newer download replaces the installed copy", code == 0 and open(inst_exe, "rb").read() == newer, out[-400:])

json.dump({"language": "en"}, open(os.path.join(DATA, "settings.json"), "w"))
code, out = run(inst_exe, "--uninstall", "--yes")
check("--uninstall --yes exits 0", code == 0, out[-600:])
check("…the Installed apps entry is gone", uninstall_values() is None, uninstall_values())
check("…the install folder is gone", not os.path.exists(INST), os.listdir(INST) if os.path.exists(INST) else None)
check("…the settings stay", os.path.exists(os.path.join(DATA, "settings.json")))

# ---------------------------------------------------------------- the first start from Downloads (tray, Xvfb)
procs = [subprocess.Popen(["Xvfb", DISPLAY, "-screen", "0", "1280x900x24"], stderr=subprocess.DEVNULL)]
time.sleep(1)
procs.append(subprocess.Popen(["stalonetray", "--geometry", "4x1+1100+0", "--icon-size", "24"], env=env,
                              stderr=subprocess.DEVNULL, stdout=subprocess.DEVNULL))
time.sleep(1)


def xdo(*args):
    return subprocess.run(["xdotool"] + list(args), env=env, capture_output=True, text=True).stdout


DIALOG = "^Mount Sheet Sync$"   # the install question; the tray's own windows are «Mount Sheet Sync — …»


def wait_window(part=DIALOG, timeout=30):
    end = time.time() + timeout
    while time.time() < end:
        for wid in xdo("search", "--onlyvisible", "--name", part).split():
            return wid
        time.sleep(0.4)
    return None


def shot(name):
    subprocess.run(["import", "-window", "root", os.path.join(W, name + ".png")], env=env, check=False)


def running(path):
    return [p for p in subprocess.run(["pgrep", "-af", "mono"], capture_output=True, text=True).stdout.splitlines() if path in p]


def answer(key):
    wid = wait_window()
    if wid is None:
        return False
    time.sleep(1)
    xdo("windowfocus", "--sync", wid)
    xdo("key", "--window", wid, key)
    return True


try:
    shutil.rmtree(DATA)
    os.makedirs(DATA)
    p = subprocess.Popen(["mono", dl_exe, "--data", DATA], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    wid = wait_window()
    time.sleep(1)
    shot("i1_offer")
    check("first start from Downloads asks to install", wid is not None)
    answer("Return")   # «Yes» is the default button
    p.wait(timeout=60)
    end = time.time() + 40
    while time.time() < end and not running(inst_exe):
        time.sleep(0.5)
    check("«Yes»: installed, and the installed copy is the one running", os.path.exists(inst_exe) and running(inst_exe)
          and not running(dl_exe), running("MountSheetSync"))
    check("…registered in Installed apps", uninstall_values() is not None)
    time.sleep(3)
    shot("i2_installed_running")

    # again from Downloads: «Update the installed…?» → Cancel: nothing changes, the installed copy keeps running
    before = open(inst_exe, "rb").read()
    open(dl_exe, "wb").write(newer + b"2")
    p = subprocess.Popen(["mono", dl_exe, "--data", DATA], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    check("a second download asks again (update the installed copy)", answer("Escape"))
    p.wait(timeout=30)
    check("«Cancel»: the installed copy is untouched and still running", open(inst_exe, "rb").read() == before and running(inst_exe))

    # «No»: runs from Downloads (here: the installed copy is already in the tray, so it just hands over) and is not asked again
    p = subprocess.Popen(["mono", dl_exe, "--data", DATA], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    wid = wait_window()
    ok = wid is not None
    time.sleep(1)
    xdo("windowfocus", "--sync", wid)
    xdo("key", "--window", wid, "Tab")   # Yes → No
    xdo("key", "--window", wid, "Return")
    time.sleep(3)
    st = json.load(open(os.path.join(DATA, "settings.json"), encoding="utf-8-sig"))
    check("«No» is remembered for this file", ok and os.path.normcase(st.get("installDeclined", "")) == os.path.normcase(dl_exe), st)
    for w in xdo("search", "--onlyvisible", "--name", DIALOG).split():
        xdo("key", "--window", w, "Return")   # «already running» note, if any
    try:
        p.wait(timeout=10)   # Windows: hands over to the copy in the tray and exits
    except subprocess.TimeoutExpired:
        p.kill()             # mono has no cross-process named mutex, so here it simply runs from Downloads
        p.wait()
    p = subprocess.Popen(["mono", dl_exe, "--data", DATA], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(6)
    asked = xdo("search", "--onlyvisible", "--name", DIALOG).split()
    check("…and the next start does not ask", not asked and p.poll() is None, asked)
    shot("i3_not_asked")
finally:
    subprocess.run(["pkill", "-f", "MountSheetSync.exe --data " + DATA], capture_output=True)
    subprocess.run(["pkill", "-f", inst_exe], capture_output=True)
    subprocess.run(["pkill", "-f", dl_exe], capture_output=True)
    for pr in procs:
        pr.kill()

failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
