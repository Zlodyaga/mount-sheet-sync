"""UI walk-through of the 2.0 first run under mono + Xvfb: settings window (Google mode) -> «Войти через Google…»
-> «Создать таблицу…» (character list) -> sheet created -> kills synced. Screenshots go to the run folder.

  python3 ui_google.py <MountSheetSync.exe>
"""
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.request
import paths

EXE = os.path.abspath(sys.argv[1])
HERE = os.path.dirname(os.path.abspath(__file__))
W = paths.work("ui_google")
PORT = 18932
BASE = "http://127.0.0.1:%d" % PORT
DISPLAY = ":97"
NOW = int(time.time())
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1200]))


shutil.rmtree(W, ignore_errors=True)
DATA = os.path.join(W, "data")
WOW = os.path.join(W, "WoW")
SV_DIR = os.path.join(WOW, "_retail_", "WTF", "Account", "ACC1", "SavedVariables")
ADDON = os.path.join(WOW, "_retail_", "Interface", "AddOns", "MountSheetSync")
for d in (DATA, SV_DIR, ADDON):
    os.makedirs(d)
# test_collection.py wrote a real SavedVariables file (two characters, collection); add a kill of this week
sv = open(os.path.join(HERE, "sv_collection.lua"), encoding="utf-8").read()
kill = ('\t["kills"] = {\n\t\t{\n\t\t\t["id"] = "K-ui-1",\n\t\t\t["time"] = %d,\n\t\t\t["char"] = "Мирна",\n'
        '\t\t\t["realm"] = "Ревущий фьорд",\n\t\t\t["boss"] = "Сильвана Ветрокрылая",\n\t\t\t["kind"] = "raid",\n'
        '\t\t\t["difficultyID"] = 16,\n\t\t\t["weekEnd"] = %d,\n\t\t},\n\t},\n' % (NOW - 60, NOW + 3 * 86400))
sv = sv.replace('\t["kills"] = {\n', kill + '\t["oldkills"] = {\n', 1) if '\t["kills"] = {\n' in sv else sv.replace("MountSheetSyncDB = {\n", "MountSheetSyncDB = {\n" + kill, 1)
open(os.path.join(SV_DIR, "MountSheetSync.lua"), "w", encoding="utf-8").write(sv)
json.dump({"wowPath": WOW, "pollSeconds": 2}, open(os.path.join(DATA, "settings.json"), "w"))
BROWSER_LOG = os.path.join(W, "browser.txt")

env = dict(os.environ, DISPLAY=DISPLAY, MSS_GOOGLE_TEST=BASE, MSS_BROWSER_LOG=BROWSER_LOG, LANG="ru_RU.UTF-8", MSS_SYSTEM_LANG="ru",
           MONO_REGISTRY_PATH=os.path.join(W, "registry"))
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY"):
    env.pop(k, None)
procs = []
procs.append(subprocess.Popen(["Xvfb", DISPLAY, "-screen", "0", "1280x900x24"], stderr=subprocess.DEVNULL))
time.sleep(1)
procs.append(subprocess.Popen(["stalonetray", "--geometry", "4x1+1100+0"], env=env, stderr=subprocess.DEVNULL, stdout=subprocess.DEVNULL))
procs.append(subprocess.Popen(["python3", os.path.join(HERE, "mock_google.py"), str(PORT), os.path.join(W, "mock.json"), os.path.join(W, "mock.log")]))
time.sleep(1)


def shot(name):
    path = os.path.join(W, name + ".png")
    subprocess.run(["import", "-window", "root", path], env=env, check=False)
    return path


def windows():
    p = subprocess.run(["xdotool", "search", "--name", "Mount Sheet Sync"], env=env, capture_output=True, text=True)
    names = []
    for wid in p.stdout.split():
        n = subprocess.run(["xdotool", "getwindowname", wid], env=env, capture_output=True, text=True).stdout.strip()
        names.append((wid, n))
    return names


def wait_window(part, timeout=40):
    end = time.time() + timeout
    while time.time() < end:
        for wid, n in windows():
            if part in n:
                return wid
        time.sleep(0.4)
    return None


def key(wid, k):
    # без оконного менеджера windowactivate не работает — фокус ставим напрямую
    subprocess.run(["xdotool", "windowfocus", "--sync", wid], env=env, capture_output=True)
    time.sleep(0.3)
    subprocess.run(["xdotool", "key", "--window", wid, k], env=env)


def state():
    return json.loads(urllib.request.urlopen(urllib.request.Request(BASE + "/__state"), timeout=10).read())


app = subprocess.Popen(["mono", EXE, "--data", DATA], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
procs.append(app)
try:
    wid = wait_window("настройки")
    check("first run opens the settings window", wid is not None, windows())
    time.sleep(1.5)
    shot("u1_settings_first_run")
    key(wid, "Return")   # focus is on «Войти через Google…»
    sid = wait_window("вход через Google", 20)
    check("«Войти через Google…» opens the sign-in window", sid is not None, windows())
    time.sleep(1)
    shot("u2_signin_wait")
    end = time.time() + 20
    while time.time() < end and not (os.path.exists(BROWSER_LOG) and open(BROWSER_LOG).read().strip()):
        time.sleep(0.3)
    url = open(BROWSER_LOG).read().strip().splitlines()[0] if os.path.exists(BROWSER_LOG) else ""
    check("the browser is asked to open Google's page", url.startswith(BASE + "/o/oauth2/v2/auth?"), url)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    page = opener.open(url, timeout=20).read().decode("utf-8")
    check("sign-in page done", "Вход выполнен" in page, page[:300])
    time.sleep(2.5)
    check("sign-in window closed", wait_window("вход через Google", 1) is None)
    shot("u3_settings_signed_in")
    key(wid, "Return")   # focus moved to «Создать таблицу…»
    cid = wait_window("новая таблица", 20)
    check("«Создать таблицу…» opens the character list", cid is not None, windows())
    time.sleep(3)
    shot("u4_characters")
    key(cid, "Return")   # «Создать таблицу»
    end = time.time() + 40
    while time.time() < end and wait_window("новая таблица", 1):
        time.sleep(0.5)
    check("character window closes after creating", wait_window("новая таблица", 1) is None)
    time.sleep(1.5)
    shot("u5_settings_created")
    s = state()
    books = list(s["spreadsheets"].values())
    check("sheet created in Google", len(books) == 1 and any(x["title"] == "Рейды Мирна" for x in books[0]["sheets"]),
          [x["title"] for b in books for x in b["sheets"]])
    st = json.load(open(os.path.join(DATA, "settings.json"), encoding="utf-8-sig"))
    check("settings saved at once (before «Сохранить»)", st.get("spreadsheetId") == (books[0]["id"] if books else None), st)
    check("the new sheet opened in the browser", any("docs.google.com/spreadsheets/d/" in l for l in open(BROWSER_LOG).read().splitlines()))
    # без оконного менеджера фокус клавиатуры после закрытия модального окна не возвращается:
    # щёлкаем по надписи в окне (фокус остаётся на «Сохранить»), потом Enter
    geo = subprocess.run(["xdotool", "getwindowgeometry", "--shell", wid], env=env, capture_output=True, text=True).stdout
    g = dict(line.split("=") for line in geo.split())
    # «Сохранить» — вторая кнопка справа внизу (смещения сняты со снимка окна)
    subprocess.run(["xdotool", "mousemove", str(int(g["X"]) + int(g["WIDTH"]) - 192),
                    str(int(g["Y"]) + int(g["HEIGHT"]) - 39), "click", "1"], env=env)
    time.sleep(1.5)
    check("settings window closed", wait_window("настройки", 1) is None)
    ok = False
    end = time.time() + 40
    while time.time() < end and not ok:
        b = state()["spreadsheets"][books[0]["id"]]
        mim = [x for x in b["sheets"] if x["title"] == "Рейды Мирна"][0]
        cells = mim["cells"]
        for k2, v in cells.items():
            r, c = map(int, k2.split(","))
            if c == 3 and v.get("stringValue") == "Отмщение":
                ok = cells.get("%d,0" % r, {}).get("stringValue") == "Да"
        time.sleep(1)
    check("the tray app sends this week's kill to the new sheet", ok)
    sd = os.path.join(ADDON, "SheetData.lua")
    end = time.time() + 30
    while time.time() < end and not (os.path.exists(sd) and "MountSheetSyncSheet = {" in open(sd, encoding="utf-8").read()):
        time.sleep(0.5)
    check("the «Remaining» list is in SheetData.lua", os.path.exists(sd) and "MountSheetSyncSheet = {" in open(sd, encoding="utf-8").read())
    # CurseForge updates the addon: its SheetData.lua is the empty one from the package
    open(sd, "w", encoding="utf-8").write("MountSheetSyncSheet = nil\n")
    end = time.time() + 20
    while time.time() < end and "MountSheetSyncSheet = nil" in open(sd, encoding="utf-8").read():
        time.sleep(0.5)
    check("an addon update that empties SheetData.lua: the program writes the list back within seconds",
          "MountSheetSyncSheet = {" in open(sd, encoding="utf-8").read())
    shot("u6_after")
    subprocess.run(["xdotool", "mousemove", "1112", "12", "click", "3"], env=env)   # меню значка в трее
    time.sleep(1.5)
    shot("u7_tray_menu")

    def menu_click(y):
        subprocess.run(["xdotool", "mousemove", "1112", "12", "click", "3"], env=env)
        time.sleep(1.2)
        subprocess.run(["xdotool", "mousemove", "900", str(y), "click", "1"], env=env)

    subprocess.run(["xdotool", "key", "Escape"], env=env)
    time.sleep(0.5)
    # «Что фармить…»: снять галочку у первой строки и сохранить
    menu_click(216)
    fid = wait_window("что фармить", 20)
    check("«Что фармить…» opens from the tray menu", fid is not None, windows())
    time.sleep(4)
    shot("u8_farm")
    geo = subprocess.run(["xdotool", "getwindowgeometry", "--shell", fid], env=env, capture_output=True, text=True).stdout
    g = dict(line.split("=") for line in geo.split())
    st0 = state()["spreadsheets"][books[0]["id"]]
    mim0 = [x for x in st0["sheets"] if x["title"] == "Рейды Мирна"][0]["cells"]
    def click_rel(dx, dy):
        subprocess.run(["xdotool", "mousemove", str(int(g["X"]) + dx), str(int(g["Y"]) + dy), "click", "1"], env=env)
        time.sleep(0.6)
    click_rel(53, 138)   # галочка «Рейды» (корень дерева) — снять все рейдовые строки персонажа
    time.sleep(0.8)
    shot("u9_farm_unticked")
    click_rel(461, 619)  # «Сохранить»
    end = time.time() + 30
    while time.time() < end and wait_window("что фармить", 1):
        time.sleep(0.5)
    st1 = state()["spreadsheets"][books[0]["id"]]
    sheets_now = {x["title"]: x["cells"] for x in st1["sheets"]}
    changed = [(t, k3) for t, c in sheets_now.items() if t.startswith("Рейды ") or t.startswith("Подземелья ")
               for k3, v in c.items() if k3.endswith(",7") and v.get("boolValue") is False]
    mim1 = sheets_now.get("Рейды Мирна", {})
    raid_rows = [k3 for k3 in mim1 if k3.endswith(",7") and not k3.startswith("0,")]
    check("«Что фармить…»: «Рейды» unticked for Мирна — every raid row saved as FALSE, other sheets untouched",
          wait_window("что фармить", 1) is None and raid_rows and all(mim1[k3].get("boolValue") is False for k3 in raid_rows)
          and all(t == "Рейды Мирна" for t, _ in changed), (len(raid_rows), changed[:5]))
    # «Персонажи в таблице…»: окно со списком, без изменений
    menu_click(193)
    pid = wait_window("персонажи в таблице", 20)
    check("«Персонажи в таблице…» opens from the tray menu", pid is not None, windows())
    time.sleep(4)
    shot("u10_characters_manage")
    key(pid, "Escape")
    time.sleep(1)
    check("…and closes with Esc", wait_window("персонажи в таблице", 1) is None)
finally:
    for p in reversed(procs):
        p.terminate()
    try:
        out = app.communicate(timeout=10)[0].decode("utf-8", "replace")
    except Exception:
        out = ""
    open(os.path.join(W, "app.out"), "w").write(out)

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
