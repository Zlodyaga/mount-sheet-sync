"""End-to-end local mode: data in a file on this computer, no Google. Created from the catalog (--create-local), the sync
loop marks kills, counts attempts and separate lockouts, characters change, a damaged file falls back to the backup; a
table from mock_google.py is copied over (--import-local) without counting its kills twice.

  python3 e2e_local.py <MountSheetSync.exe>
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
W = paths.work("e2e_local")
PORT = 18935
BASE = "http://127.0.0.1:%d" % PORT
NOW = int(time.time())
WEEK_END = NOW + 3 * 86400
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


shutil.rmtree(W, ignore_errors=True)
WOW = os.path.join(W, "WoW")
SV_DIR = os.path.join(WOW, "_retail_", "WTF", "Account", "ACC1", "SavedVariables")
ADDON = os.path.join(WOW, "_retail_", "Interface", "AddOns", "MountSheetSync")
for d in (SV_DIR, ADDON):
    os.makedirs(d)
open(os.path.join(ADDON, "MountSheetSync.toc"), "w").write("## Interface: 120105\n")
ENV = dict(os.environ, LANG="C.UTF-8", MSS_SYSTEM_LANG="ru", MSS_BROWSER_LOG=os.path.join(W, "browser.txt"))
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
    ENV.pop(k, None)
# local mode must not need Google: nothing listens here, so any request would fail and show up in the log
ENV["MSS_GOOGLE_TEST"] = "http://127.0.0.1:%d" % (PORT + 1)


def new_data(name):
    d = os.path.join(W, name)
    os.makedirs(d)
    json.dump({"wowPath": WOW, "pollSeconds": 2}, open(os.path.join(d, "settings.json"), "w"))
    return d


def run(data, *args, env=None):
    # what the program logged during this run: its console output depends on the system's code page
    log = os.path.join(data, "sync.log")
    seen = os.path.getsize(log) if os.path.exists(log) else 0
    p = subprocess.run(["mono", EXE, "--data", data] + list(args), env=env or ENV, capture_output=True, timeout=300)
    text = ""
    if os.path.exists(log):
        with open(log, "rb") as f:
            f.seek(seen)
            text = f.read().decode("utf-8", "replace")
    return p.returncode, text


def lua(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return str(v)
    return '"' + str(v).replace("\\", "\\\\").replace('"', '\\"') + '"'


CHARS = [{"name": n, "realm": "Ревущий фьорд", "class": "Охотница", "classFile": "HUNTER", "level": 90, "seen": NOW - 100}
         for n in ("Эйра", "Мирна")]


def write_sv(kills=(), chars=CHARS):
    L = ["MountSheetSyncDB = {", '\t["chars"] = {']
    for c in chars:
        L.append("\t\t[%s] = {" % lua(c["name"] + "-" + c["realm"]))
        L += ["\t\t\t[%s] = %s," % (lua(k), lua(v)) for k, v in c.items()]
        L.append("\t\t},")
    L += ["\t},", '\t["collection"] = { ["updated"] = %d, ["journal"] = 10, ["collected"] = 1, ["names"] = { "Пепел Ал\'ара" }, ["ids"] = { 183 }, ["mounts"] = {} },' % (NOW - 3600),
          '\t["kills"] = {']
    for k in kills:
        L.append("\t\t{ " + " ".join("[%s] = %s," % (lua(a), lua(b)) for a, b in k.items()) + " },")
    L += ["\t},", "}"]
    open(os.path.join(SV_DIR, "MountSheetSync.lua"), "w", encoding="utf-8", newline="\r\n").write("\n".join(L) + "\n")


def kill(uid, boss, diff=16, char="Эйра"):
    return {"id": uid, "time": NOW - 60, "char": char, "realm": "Ревущий фьорд", "charKey": "Player-1-A", "source": "encounter",
            "boss": boss, "kind": "raid", "difficultyID": diff, "weekEnd": WEEK_END, "instance": "Святилище Господства"}


def book(data):
    return json.load(open(os.path.join(data, "local-data.json"), encoding="utf-8"))


def sheet(data, name):
    return [s for s in book(data)["sheets"] if s["name"] == name][0]["rows"]


def row_of(rows, boss):
    return [r for r in rows if any(isinstance(v, str) and v.startswith(boss) for v in r)][0]


def column(rows, title):
    return [i for i, v in enumerate(rows[0]) if str(v).lower().startswith(title)][0]


def attempts(data, boss):
    rows = sheet(data, "Рейды")
    return row_of(rows, boss)[column(rows, "попыток")]


def sheet_lua():
    return open(os.path.join(ADDON, "SheetData.lua"), encoding="utf-8").read()


# ---------------------------------------------------------------- a new player: nothing to set up
N = new_data("newplayer")
write_sv(chars=[])
code, out = run(N, "--once")
check("a new player: works before the addon knows anyone, no file yet", code == 0 and not os.path.exists(os.path.join(N, "local-data.json")), out[-600:])
settings = json.load(open(os.path.join(N, "settings.json"), encoding="utf-8-sig"))
check("…no sign-in asked", "Не выполнен вход" not in out and "войдите" not in out, out[-400:])
write_sv([kill("N1", "Сильвана Ветрокрылая")])
code, out = run(N, "--once")
check("the addon knows characters: the data makes itself", code == 0 and os.path.exists(os.path.join(N, "local-data.json")) and "созданы на этом компьютере" in out, out[-600:])
check("…and the kill from the journal is marked at once", "Да" in row_of(sheet(N, "Рейды Эйра"), "Сильвана Ветрокрылая"), row_of(sheet(N, "Рейды Эйра"), "Сильвана Ветрокрылая"))
check("…the list is there for the addon", 'name = "Эйра"' in sheet_lua())
O = new_data("oldplayer")
settings = json.load(open(os.path.join(O, "settings.json"), encoding="utf-8-sig"))
settings["spreadsheetId"] = "abc"
json.dump(settings, open(os.path.join(O, "settings.json"), "w"))
code, out = run(O, "--once")
check("settings with a table and no mode stay on Google, nothing local appears", not os.path.exists(os.path.join(O, "local-data.json")), out[-400:])

# ---------------------------------------------------------------- created here, no Google at all
A = new_data("local")
write_sv()
code, out = run(A, "--create-local")
check("--create-local: created without Google", code == 0 and os.path.exists(os.path.join(A, "local-data.json")), out[-800:])
settings = json.load(open(os.path.join(A, "settings.json"), encoding="utf-8-sig"))
check("…and switched to it", settings.get("mode") == "local", settings.get("mode"))
b = book(A)
names = [s["name"] for s in b["sheets"]]
check("the same sheets as a new table", all(n in names for n in ("Рейды", "Рейды Эйра", "Рейды Мирна", "Подземелья", "Подземелья Эйра", "Мировые боссы", "Сводка")), names)
check("layout is current, so the sync loop has nothing to upgrade", b["properties"].get("layout") == "5", b["properties"])
rows = sheet(A, "Рейды Эйра")
check("character sheet: «Девятка» row from the catalog, with «Фармлю»", any("Девятка" in r for r in rows) and "Фармлю" in rows[0], rows[0])
check("no default sheet and no empty rows", all(s["rows"] and all(len(r) == len(s["rows"][0]) for r in s["rows"]) for s in b["sheets"]))
code, out = run(A, "--create-local")
check("a second --create-local replaces nothing", code == 1 and "уже есть" in out, out[-400:])

code, out = run(A, "--test")
check("--test works", code == 0, out[-500:])
code, out = run(A, "--once")
check("--once: list made, no upgrade, Google never asked", code == 0 and "Таблица обновлена" not in out and "нет связи" not in out, out[-800:])
check("SheetData.lua for the addon", "Сильвана Ветрокрылая" in sheet_lua() and 'name = "Эйра"' in sheet_lua(), sheet_lua()[:300])
program_lua = open(os.path.join(ADDON, "Program.lua"), encoding="utf-8").read()
check("Program.lua: the addon sees the program as connected", "sheet = true" in program_lua.replace('["sheet"]', "sheet"), program_lua[:300])

before = attempts(A, "Сильвана Ветрокрылая")
write_sv([kill("K1", "Сильвана Ветрокрылая")])
code, out = run(A, "--once")
rows = sheet(A, "Рейды Эйра")
check("a kill marks the row of that character", code == 0 and "Да" in row_of(rows, "Сильвана Ветрокрылая"), (out[-500:], row_of(rows, "Сильвана Ветрокрылая")))
check("…not of another one", "Да" not in row_of(sheet(A, "Рейды Мирна"), "Сильвана Ветрокрылая")[:3])
check("…and counts an attempt", attempts(A, "Сильвана Ветрокрылая") == (before or 0) + 1, (before, attempts(A, "Сильвана Ветрокрылая")))
check("the previous version stays as a backup", os.path.exists(os.path.join(A, "local-data.json.bak")))
sent = open(os.path.join(A, "sent-ids-local.txt"), encoding="utf-8").read()
check("sent records have their own list", "K1" in sent, sent[:200])
code, out = run(A, "--once")
check("the same record is not counted again", attempts(A, "Сильвана Ветрокрылая") == (before or 0) + 1)

# ---------------------------------------------------------------- separate lockouts
kills = [kill("K1", "Сильвана Ветрокрылая"), kill("K2", "Девятка", 14)]
write_sv(kills)
code, out = run(A, "--once")
rows = sheet(A, "Рейды Эйра")
parts = column(rows, "n/h/m")
check("Normal kill of a Normal/Heroic/Mythic row: the letter is kept, the row stays open", code == 0 and row_of(rows, "Девятка")[parts] == "N" and "Да" not in row_of(rows, "Девятка"), row_of(rows, "Девятка"))
check("…the list tells the addon", '["sep"]' in sheet_lua() or "sep" in sheet_lua())
kills += [kill("K3", "Девятка", 15), kill("K4", "Девятка", 16)]
write_sv(kills)
code, out = run(A, "--once")
rows = sheet(A, "Рейды Эйра")
check("all three: the row closes", "Да" in row_of(rows, "Девятка"), row_of(rows, "Девятка"))

# ---------------------------------------------------------------- characters and the collection
spec = [{"name": "Эйра", "raids": True, "dungeons": True, "world": True}, {"name": "Новая", "raids": True, "dungeons": False, "world": True}]
path = os.path.join(W, "chars.json")
json.dump(spec, open(path, "w", encoding="utf-8"), ensure_ascii=False)
code, out = run(A, "--set-chars", path)
b = book(A)
names = [s["name"] for s in b["sheets"]]
check("--set-chars: new sheet, the old one gone", code == 0 and "Рейды Новая" in names and "Рейды Мирна" not in names and "Подземелья Новая" not in names, (names, out[-500:]))
world = sheet(A, "Мировые боссы")
check("…world columns follow", "Новая" in world[0] and "Мирна" not in world[0] and "Эйра" in world[0], world[0])
check("…and every world row has the new tick box", all(r[world[0].index("Новая")] is False for r in world[1:] if r[3]), [r[world[0].index("Новая")] for r in world[1:4]])
check("Эйра keeps her marks", "Да" in row_of(sheet(A, "Рейды Эйра"), "Сильвана Ветрокрылая"))
code, out = run(A, "--sync-collection")
check("--sync-collection works", code == 0 and "Сверка с коллекцией" in out, out[-500:])

# ---------------------------------------------------------------- a damaged file
good = open(os.path.join(A, "local-data.json"), encoding="utf-8").read()
open(os.path.join(A, "local-data.json"), "w", encoding="utf-8").write(good[:len(good) // 2])
write_sv(kills + [kill("K5", "Тюремщик")])
code, out = run(A, "--once")
check("a damaged file: the backup is read and the work goes on", code == 0 and "резервную копию" in out, out[-800:])
check("…the file is whole again", json.loads(open(os.path.join(A, "local-data.json"), encoding="utf-8").read())["format"] == 1)
check("…the damaged one is kept aside", os.path.exists(os.path.join(A, "local-data.json.damaged")))
check("…and the backup is not overwritten by it", json.loads(open(os.path.join(A, "local-data.json.bak"), encoding="utf-8").read())["format"] == 1)

# ---------------------------------------------------------------- another language
E = new_data("english")
write_sv()
code, out = run(E, "--create-local", "--sheet-lang", "en")
check("English: created", code == 0, out[-500:])
check("…sheets are kept in the program's inner form", "Рейды Эйра" in [s["name"] for s in book(E)["sheets"]] and book(E)["lang"] == "en", [s["name"] for s in book(E)["sheets"]][:3])
code, out = run(E, "--once")
check("…the addon gets the English names", code == 0 and 'spreadsheet = "WoW Mounts"' in sheet_lua() and "Sylvanas Windrunner" in sheet_lua(), sheet_lua()[:400])

# ---------------------------------------------------------------- a table from Google moves here
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
G = new_data("google")
mock = subprocess.Popen([sys.executable, os.path.join(HERE, "mock_google.py"), str(PORT), os.path.join(W, "mock_state.json"), os.path.join(W, "mock.log")])
time.sleep(0.8)
GENV = dict(ENV, MSS_GOOGLE_TEST=BASE)
try:
    write_sv([kill("G1", "Сильвана Ветрокрылая")])
    code, out = run(G, "--import-local", env=GENV)
    check("nothing to move without a table", code == 1 and "переносить нечего" in out, out[-400:])
    proc = subprocess.Popen(["mono", EXE, "--data", G, "--google-login"], env=GENV, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    browser = ENV["MSS_BROWSER_LOG"]
    deadline = time.time() + 60
    while time.time() < deadline and proc.poll() is None:
        if os.path.exists(browser) and open(browser, encoding="utf-8").read().strip():
            OPENER.open(open(browser, encoding="utf-8").read().strip().splitlines()[-1], timeout=20).read()
            break
        time.sleep(0.2)
    proc.communicate(timeout=60)
    code, out = run(G, "--create-sheet", "Маунты", env=GENV)
    check("a table in Google", code == 0, out[-500:])
    code, out = run(G, "--once", env=GENV)
    check("…with one kill in it", code == 0, out[-500:])
    state = lambda: json.loads(OPENER.open(BASE + "/__state", timeout=10).read())
    google_book = list(state()["spreadsheets"].values())[0]
    code, out = run(G, "--import-local", env=GENV)
    check("--import-local: copied and switched", code == 0 and json.load(open(os.path.join(G, "settings.json"), encoding="utf-8-sig")).get("mode") == "local", out[-500:])
    check("the kill came along", "Да" in row_of(sheet(G, "Рейды Эйра"), "Сильвана Ветрокрылая"))
    n = attempts(G, "Сильвана Ветрокрылая")
    check("…and its attempt", n == 1, n)
    code, out = run(G, "--once", env=ENV)   # no Google from here on
    check("already sent records are not counted again, and Google is not needed", code == 0 and attempts(G, "Сильвана Ветрокрылая") == 1, (out[-500:], attempts(G, "Сильвана Ветрокрылая")))
    write_sv([kill("G1", "Сильвана Ветрокрылая"), kill("G2", "Тюремщик")])
    code, out = run(G, "--once", env=ENV)
    check("a new kill stays here", code == 0 and "Да" in row_of(sheet(G, "Рейды Эйра"), "Тюремщик"), out[-400:])
    check("…the table in Google is not touched", list(state()["spreadsheets"].values())[0] == google_book)
    code, out = run(G, "--import-local", env=GENV)
    check("a second copy replaces nothing", code == 1 and "уже есть" in out, out[-400:])
    code, out = run(G, "--storage", "google", env=GENV)
    check("--storage google: back to the table", code == 0 and json.load(open(os.path.join(G, "settings.json"), encoding="utf-8-sig")).get("mode") == "google", out[-400:])
    code, out = run(G, "--storage", "local", env=GENV)
    check("--storage local: and here again, the data is still there", code == 0 and "Да" in row_of(sheet(G, "Рейды Эйра"), "Сильвана Ветрокрылая"), out[-400:])
    code, out = run(G, "--storage", "disk", env=GENV)
    check("an unknown storage is refused", code == 1, out[-300:])
finally:
    mock.terminate()

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
