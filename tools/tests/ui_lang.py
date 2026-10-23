"""2.3: the tray program speaks the Windows language when it has it (en, ru, de, fr, es, pt), English otherwise;
a choice in the tray menu («Language») wins and is saved. Runs under mono + Xvfb; screenshots go to the run folder.

  python3 ui_lang.py <MountSheetSync.exe>
"""
import json
import os
import shutil
import subprocess
import sys
import time
import paths

EXE = os.path.abspath(sys.argv[1])
W = paths.work("ui_lang")
DISPLAY = ":96"
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1200]))


shutil.rmtree(W, ignore_errors=True)
os.makedirs(W)
base_env = dict(os.environ, DISPLAY=DISPLAY, LANG="C.UTF-8", MONO_REGISTRY_PATH=os.path.join(W, "registry"))
procs = [subprocess.Popen(["Xvfb", DISPLAY, "-screen", "0", "1280x900x24"], stderr=subprocess.DEVNULL)]
time.sleep(1)
procs.append(subprocess.Popen(["stalonetray", "--geometry", "4x1+1100+0", "--icon-size", "24"], env=base_env,
                              stderr=subprocess.DEVNULL, stdout=subprocess.DEVNULL))
time.sleep(1)


def xdo(*args):
    return subprocess.run(["xdotool"] + list(args), env=base_env, capture_output=True, text=True).stdout


def shot(name):
    subprocess.run(["import", "-window", "root", os.path.join(W, name + ".png")], env=base_env, check=False)


def titles():
    return [xdo("getwindowname", wid).strip() for wid in xdo("search", "--name", "Mount Sheet Sync").split()]


def wait_title(part, timeout=40):
    end = time.time() + timeout
    while time.time() < end:
        for t in titles():
            if part in t:
                return t
        time.sleep(0.4)
    return None


def run(name, system_lang, settings=None):
    data = os.path.join(W, name)
    os.makedirs(data)
    if settings is not None:
        json.dump(settings, open(os.path.join(data, "settings.json"), "w"))
    env = dict(base_env, MSS_SYSTEM_LANG=system_lang)
    return data, subprocess.Popen(["mono", EXE, "--data", data], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)


def stop(p):
    p.kill()
    p.wait()
    time.sleep(1)


def log_text(data):
    path = os.path.join(data, "sync.log")
    return open(path, encoding="utf-8-sig").read() if os.path.exists(path) else ""


try:
    # 1. Windows in German → the first-run settings window is German, the log is English
    data, p = run("de", "de")
    t = wait_title("Einstellungen")
    check("German Windows: settings window in German", t == "Mount Sheet Sync — Einstellungen", titles())
    time.sleep(2)
    shot("l1_settings_de")
    log = log_text(data)
    check("…journal lines in English (only en/ru there)", "started. Settings and log:" in log and "запущен" not in log, log[-400:])
    stop(p)

    # 2. a language the program does not have → English
    data, p = run("zh", "zh")
    t = wait_title("settings")
    check("Chinese Windows: English", t == "Mount Sheet Sync — settings", titles())
    stop(p)

    # 3. Russian Windows → Russian
    data, p = run("ru", "ru")
    t = wait_title("настройки")
    check("Russian Windows: Russian", t == "Mount Sheet Sync — настройки", titles())
    log = log_text(data)
    check("…and a Russian journal", "запущен. Настройки и журнал:" in log, log[-300:])
    stop(p)

    # 4. saved choice beats the Windows language
    data, p = run("fr", "de", {"language": "fr", "pollSeconds": 2})
    t = wait_title("paramètres")
    check("settings «language: fr» on German Windows: French", t == "Mount Sheet Sync — paramètres", titles())
    time.sleep(2)
    shot("l2_settings_fr")
    stop(p)

    # 5. the tray menu: «Language» submenu, pick Español → saved, menu in Spanish at once
    data, p = run("menu", "en", {"pollSeconds": 2, "mode": "google"})
    t = wait_title("settings")
    check("first run in English", t == "Mount Sheet Sync — settings", titles())
    time.sleep(2)
    xdo("mousemove", "1112", "12")
    xdo("click", "3")
    time.sleep(2)
    shot("l3_menu_en")
    st_before = json.load(open(os.path.join(data, "settings.json"), encoding="utf-8-sig"))
    # the menu opens upwards/downwards depending on room; walk it with the keyboard: Up from the bottom
    # (Exit, separator skipped, Language) and Right into the submenu
    xdo("key", "Up", "Up")
    time.sleep(0.6)
    xdo("key", "Right")
    time.sleep(1)
    shot("l4_language_submenu")
    # submenu: Same as Windows, English, Русский, Deutsch, Français, Español, Português → Down ×5 from the first
    for _ in range(5):
        xdo("key", "Down")
        time.sleep(0.2)
    xdo("key", "Return")
    time.sleep(2)
    st = json.load(open(os.path.join(data, "settings.json"), encoding="utf-8-sig"))
    check("menu pick saved: language = es", st.get("language") == "es", (st_before.get("language"), st.get("language")))
    check("…and logged in English (Spanish is not a journal language)", "Program language: Español." in log_text(data),
          log_text(data)[-300:])
    xdo("mousemove", "1112", "12")
    xdo("click", "3")
    time.sleep(2)
    shot("l5_menu_es")
    xdo("key", "Escape")
    stop(p)
    # 6. WoW is there, the addon is not (first run on someone else's PC): say so, in the chosen language
    wow = os.path.join(W, "WoW")
    os.makedirs(os.path.join(wow, "_retail_", "WTF", "Account", "ACC1", "SavedVariables"))
    data, p = run("noaddon", "de", {"wowPath": wow, "pollSeconds": 2, "mode": "script",
                                    "webAppUrl": "http://127.0.0.1:9/exec", "secret": "x"})
    end = time.time() + 40
    while time.time() < end and "not installed" not in log_text(data):
        time.sleep(0.5)
    check("no addon folder: the log says the addon is not installed", "addon is not installed: there is no folder" in log_text(data),
          log_text(data)[-600:])
    time.sleep(1)
    shot("l6_no_addon")
    stop(p)
    data2, p = run("menu2", "en", dict(st))
    check("after a restart the program stays in Spanish", wait_title("ajustes") == "Mount Sheet Sync — ajustes", titles())
    stop(p)
finally:
    for pr in procs:
        try:
            pr.kill()
        except Exception:
            pass

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
raise SystemExit(0 if passed == len(checks) else 1)
