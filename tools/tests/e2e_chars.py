"""End-to-end 2.2 against mock_google.py: a 2.1 table brought to the 2.2 look by the sync loop; characters changed
(«Персонажи в таблице…» via --set-chars): sheets and world columns removed and added, formulas rewritten; the
«не фармлю» record from the game unticks «Фармлю»; a fresh 2.2 table.

  python3 e2e_chars.py <MountSheetSync 2.2.exe> <MountSheetSync 2.1.exe>
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
OLD = os.path.abspath(sys.argv[2])
HERE = os.path.dirname(os.path.abspath(__file__))
W = paths.work("e2e_chars")
PORT = 18934
BASE = "http://127.0.0.1:%d" % PORT
NOW = int(time.time())
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


shutil.rmtree(W, ignore_errors=True)
DATA = os.path.join(W, "data")
WOW = os.path.join(W, "WoW")
SV_DIR = os.path.join(WOW, "_retail_", "WTF", "Account", "ACC1", "SavedVariables")
ADDON = os.path.join(WOW, "_retail_", "Interface", "AddOns", "MountSheetSync")
for d in (DATA, SV_DIR, ADDON):
    os.makedirs(d)
open(os.path.join(ADDON, "MountSheetSync.toc"), "w").write("## Interface: 120105\n")
json.dump({"wowPath": WOW, "pollSeconds": 2}, open(os.path.join(DATA, "settings.json"), "w"))
BROWSER_LOG = os.path.join(W, "browser.txt")
mock = subprocess.Popen(["python3", os.path.join(HERE, "mock_google.py"), str(PORT), os.path.join(W, "mock_state.json"),
                         os.path.join(W, "mock.log")])
time.sleep(0.8)
ENV = dict(os.environ, MSS_GOOGLE_TEST=BASE, MSS_BROWSER_LOG=BROWSER_LOG, LANG="C.UTF-8", MSS_SYSTEM_LANG="ru")
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
    ENV.pop(k, None)
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def state():
    return json.loads(OPENER.open(BASE + "/__state", timeout=10).read())


def run(exe, *args):
    p = subprocess.run(["mono", exe, "--data", DATA] + list(args), env=ENV, capture_output=True, timeout=300)
    return p.returncode, (p.stdout + p.stderr).decode("utf-8", "replace")


def login(exe):
    proc = subprocess.Popen(["mono", exe, "--data", DATA, "--google-login"], env=ENV, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    deadline = time.time() + 60
    while time.time() < deadline and proc.poll() is None:
        if os.path.exists(BROWSER_LOG) and open(BROWSER_LOG, encoding="utf-8").read().strip():
            OPENER.open(open(BROWSER_LOG, encoding="utf-8").read().strip().splitlines()[-1], timeout=20).read()
            break
        time.sleep(0.2)
    proc.communicate(timeout=60)
    return proc.returncode


def lua(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return str(v)
    return '"' + str(v).replace("\\", "\\\\").replace('"', '\\"') + '"'


CHARS = [{"name": n, "realm": "Ревущий фьорд", "class": "Охотница", "classFile": "HUNTER", "level": 90, "seen": NOW - 100}
         for n in ("Эйра", "Мирна", "Элтара")]


def write_sv(chars, kills=()):
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


def cells(book, title):
    s = [x for x in book["sheets"] if x["title"] == title][0]
    rows = {}
    for k, v in s["cells"].items():
        r, c = map(int, k.split(","))
        rows.setdefault(r, {})[c] = v.get("stringValue", v.get("numberValue", v.get("boolValue", v.get("formulaValue"))))
    return rows, s


def titles(book):
    return [x["title"] for x in book["sheets"]]


def set_chars(spec):
    path = os.path.join(W, "chars.json")
    json.dump(spec, open(path, "w", encoding="utf-8"), ensure_ascii=False)
    return run(EXE, "--set-chars", path)


try:
    # ---------------------------------------------------------------- a 2.1 table, then the 2.2 sync loop
    check("signed in", login(EXE) == 0)
    write_sv(CHARS[:2])
    code, out = run(OLD, "--create-sheet", "Маунты 2.1")
    check("2.1 made the table", code == 0, out[-800:])
    book = list(state()["spreadsheets"].values())[0]
    sid = book["id"]
    rows, sh = cells(book, "Рейды Эйра")
    check("2.1 layout: 7 columns, no «Фармлю»", max(rows[0]) == 6 and sh["cols"] == 7, rows[0])
    code, out = run(EXE, "--once")
    book = state()["spreadsheets"][sid]
    rows, sh = cells(book, "Рейды Эйра")
    n_rows = len([r for r in rows if r > 0])
    check("2.2 --once: «Фармлю» appended to character sheets, all ticked", rows[0].get(7) == "Фармлю" and sh["cols"] == 8
          and all(rows[r].get(7) is True for r in rows if r > 0) and n_rows > 5, (rows[0], sh["cols"]))
    check("log says the table was updated", "Таблица обновлена: «Да»/«Нет» цветом, колонка «Фармлю» на листах персонажей (4)" in out, out[-1500:])
    raids, _ = cells(book, "Рейды")
    check("raid «left» formula now asks for «Фармлю» = TRUE (in ru_RU form)", "'Рейды Эйра'!H:H; TRUE" in raids[1][8] and "'Рейды Мирна'!H:H; TRUE" in raids[1][8], raids[1][8])
    meta = {m["metadataKey"]: m["metadataValue"] for m in book["devmeta"]}
    check("layout 4 remembered in the table", meta.get("mountSheetSync.layout") == "4", meta)
    code, out = run(EXE, "--once")
    check("second run: no second upgrade", "Таблица обновлена" not in out and cells(state()["spreadsheets"][sid], "Рейды Эйра")[1]["cols"] == 8)

    # ---------------------------------------------------------------- «не фармлю» from the game
    first = rows[1]
    plan = {"id": "P-Player-1-A-%d-1" % NOW, "time": NOW - 20, "char": "Эйра", "realm": "Ревущий фьорд", "charKey": "Player-1-A",
            "source": "plan", "kind": "plan", "list": "raid", "scope": "row", "boss": first[4], "mount": first[3],
            "diff": first[5], "place": first[2], "farm": False}
    write_sv(CHARS[:2], [plan])
    code, out = run(EXE, "--once")
    rows, _ = cells(state()["spreadsheets"][sid], "Рейды Эйра")
    check("plan record unticks «Фармлю» of that row only", rows[1].get(7) is False and all(rows[r].get(7) is True for r in rows if r > 1)
          and "[-] Эйра: не фармит «%s»" % first[4] in out, (rows[1], out[-800:]))
    mim, _ = cells(state()["spreadsheets"][sid], "Рейды Мирна")
    check("…not on another character", all(mim[r].get(7) is True for r in mim if r > 0))

    # ---------------------------------------------------------------- characters: remove / change / add
    world0, wsh = cells(state()["spreadsheets"][sid], "Мировые боссы")
    check("world bosses: a column per character", [world0[0].get(c) for c in (12, 13)] == ["Мирна", "Эйра"], world0[0])
    code, out = set_chars([{"name": "Эйра", "raids": True, "dungeons": False, "world": True},
                           {"name": "Мирна", "raids": False, "dungeons": False, "world": False},
                           {"name": "Элтара", "raids": True, "dungeons": True, "world": False}])
    check("--set-chars exits 0 and tells what changed", code == 0 and all(n in out.split("удалены листы: ")[1].split(";")[0]
          for n in ("Подземелья Эйра", "Рейды Мирна", "Подземелья Мирна")) and "новые листы: Рейды Элтара, Подземелья Элтара" in out
          and "убраны колонки на «Мировых боссах»: Мирна" in out, out[-1200:])
    book = state()["spreadsheets"][sid]
    t = titles(book)
    check("sheets now: Эйра raids; Элтара raids + dungeons next to the others; Мирна gone",
          t == ["Рейды", "Рейды Эйра", "Рейды Элтара", "Подземелья", "Подземелья Элтара", "Сводка", "Мировые боссы"], t)
    world, wsh = cells(book, "Мировые боссы")
    check("world: Мирна's column deleted, Эйра' column (with its ticks) kept, no column for Элтара",
          world[0].get(12) == "Эйра" and 13 not in world[0] and wsh["cols"] == 13, (world[0], wsh["cols"]))
    check("world «left» formula covers the one remaining column", "M2:M" in world[1][6] and "N2:N" not in world[1][6], world[1][6])
    raids, _ = cells(book, "Рейды")
    check("raid «left» formula: Эйра and Элтара, not Мирна", "'Рейды Эйра'" in raids[1][8] and "'Рейды Элтара'" in raids[1][8]
          and "Мирна" not in raids[1][8], raids[1][8])
    el, esh = cells(book, "Рейды Элтара")
    check("new sheet in the 2.2 layout: «Фармлю» ticked, colours", el[0].get(7) == "Фармлю" and all(el[r].get(7) is True for r in el if r > 0)
          and esh["cols"] == 8, el[0])
    rows, _ = cells(book, "Рейды Эйра")
    check("Эйра' raid sheet untouched (her «не фармлю» stays)", rows[1].get(7) is False)
    code, out = set_chars([{"name": "Эйра", "raids": True, "dungeons": False, "world": True},
                           {"name": "Элтара", "raids": True, "dungeons": True, "world": True},
                           {"name": "Мирна", "raids": True, "dungeons": False, "world": True}])
    book = state()["spreadsheets"][sid]
    world, wsh = cells(book, "Мировые боссы")
    check("adding back: world columns for Элтара and Мирна at the right, formula over three", code == 0 and [world[0].get(c) for c in (12, 13, 14)] == ["Эйра", "Элтара", "Мирна"]
          and "O2:O" in world[1][6] and world[1].get(13) is False, (world[0], world[1].get(6)))
    check("…Мирна's raid sheet created again after the others", titles(book)[:4] == ["Рейды", "Рейды Эйра", "Рейды Элтара", "Рейды Мирна"], titles(book))
    code, out = set_chars([{"name": "Эйра", "raids": True, "dungeons": False, "world": True},
                           {"name": "Элтара", "raids": True, "dungeons": True, "world": True},
                           {"name": "Мирна", "raids": True, "dungeons": False, "world": True}])
    check("same list again: nothing to do", code == 0 and "без изменений" in out, out[-400:])

    # ---------------------------------------------------------------- a fresh 2.2 table
    write_sv(CHARS)
    code, out = run(EXE, "--create-sheet", "Маунты 2.2")
    nb = [b for b in state()["spreadsheets"].values() if b["title"] == "Маунты 2.2"][0]
    rows, sh = cells(nb, "Подземелья Мирна")
    check("new table: «Фармлю» on character sheets", code == 0 and rows[0].get(7) == "Фармлю" and sh["cols"] == 8, rows[0])
    meta = {m["metadataKey"]: m["metadataValue"] for m in nb["devmeta"]}
    check("new table: layout 4 written at creation", meta.get("mountSheetSync.layout") == "4", meta)
    settings = json.load(open(os.path.join(DATA, "settings.json"), encoding="utf-8-sig"))
    code, out = run(EXE, "--once")
    check("…so the sync loop does not «upgrade» it", code == 0 and "Таблица обновлена" not in out, out[-600:])
    raids, _ = cells(nb, "Рейды")
    check("new table: raid «left» formula with «Фармлю» (written before the switch to ru_RU)", "'Рейды Эйра'!H:H, TRUE" in raids[1][8], raids[1][8])
finally:
    mock.terminate()

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
