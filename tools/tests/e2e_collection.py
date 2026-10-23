"""End-to-end 2.0 → 2.1: a table made by MountSheetSync 2.0 is brought up to date by 2.1 against mock_google.py.

Old table (2.0 exe, addon 2.0 data) → «--sync-collection» (2.1 exe, addon 2.1 data with the whole journal):
hidden «ID в игре» column, names as in the game, «Собрано?» by mountID, collected rows gone from character
sheets; then a new mount from the game, a hand-made «Да», and a fresh 2.1 table.

  python3 e2e_collection.py <MountSheetSync 2.1.exe> <MountSheetSync 2.0.exe>
"""
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import paths

EXE = os.path.abspath(sys.argv[1])
OLD = os.path.abspath(sys.argv[2])
HERE = os.path.dirname(os.path.abspath(__file__))
W = paths.work("e2e_collection")
PORT = 18933
BASE = "http://127.0.0.1:%d" % PORT
NOW = int(time.time())
ID = "ID в игре"
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


# ---------------------------------------------------------------- the mounts used (from the real catalog)
cat = json.load(open(os.path.join(paths.PROGRAM, "catalog.json"), encoding="utf-8-sig"))
raids = [s for s in cat["sheets"] if s["key"] == "raids"][0]
RC = raids["columns"]
aliases = {int(k): v for k, v in cat.get("aliases", {}).items()}
farm = [r for r in raids["rows"] if r[RC.index("Тип источника")] == "Босс-дроп" and r[RC.index("Статус")] == "Фармится"
        and isinstance(r[RC.index(ID)], (int, float)) and r[RC.index(ID)] > 0]
ids_seen = {}
for r in farm:
    ids_seen[int(r[RC.index(ID)])] = ids_seen.get(int(r[RC.index(ID)]), 0) + 1
farm = [r for r in farm if ids_seen[int(r[RC.index(ID)])] == 1]   # one row per mount — simpler expectations
# names in the 2.0 catalog (same row order): a «renamed» row had another name in this very sheet
old_cat = json.load(open(os.path.join(paths.FIXTURES, "catalog-2.0.json"), encoding="utf-8-sig"))
old_raids = [s for s in old_cat["sheets"] if s["key"] == "raids"][0]["rows"]
old_name = {id(r): old_raids[i][old_raids and 2] for i, r in enumerate(raids["rows"])}
renamed = [r for r in farm if old_name[id(r)].lower() != r[RC.index("Маунт")].lower()]
case_only = [r for r in farm if old_name[id(r)] != r[RC.index("Маунт")] and old_name[id(r)].lower() == r[RC.index("Маунт")].lower()]
plain = [r for r in farm if int(r[RC.index(ID)]) not in aliases]
if not renamed or len(plain) < 5:
    print("catalog has no IDs / aliases yet — nothing to test")
    sys.exit(1)


def M(r):
    return {"id": int(r[RC.index(ID)]), "name": r[RC.index("Маунт")], "boss": r[RC.index("Босс / достижение / сундук")]}


A, C, D, E, F = (M(r) for r in plain[:5])   # A: same name before and after; C: learned later; D: new mount; E: «Да» by hand
B = M(renamed[0])
B["old"] = old_name[id(renamed[0])]
print("A=%s  B=%s (was %s)  C=%s  D=%s  E=%s" % (A["name"], B["name"], B["old"], C["name"], D["name"], E["name"]))

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


def api(path, body=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(),
                                 method="GET" if body is None else "POST", headers=headers)
    return json.loads(OPENER.open(req, timeout=10).read())


def state():
    return api("/__state")


def run(exe, *args):
    p = subprocess.run(["mono", exe, "--data", DATA] + list(args), env=ENV, capture_output=True, timeout=300)
    return p.returncode, (p.stdout + p.stderr).decode("utf-8", "replace")


def login(exe):
    if os.path.exists(BROWSER_LOG):
        os.remove(BROWSER_LOG)
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
    return '"' + str(v).replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") + '"'


EIRA = {"name": "Эйра", "realm": "Ревущий фьорд", "class": "Охотница", "classFile": "HUNTER", "level": 90, "seen": NOW - 100}
MIRNA = {"name": "Мирна", "realm": "Ревущий фьорд", "class": "Жрица", "classFile": "PRIEST", "level": 90, "seen": NOW - 50}


def write_sv(collected, journal, updated, kills=(), v21=True):
    """collected: mounts with id/name; journal: all mounts known (dicts) — 2.1 writes them as «mounts»."""
    L = ["MountSheetSyncDB = {", '\t["chars"] = {']
    for c in (EIRA, MIRNA):
        L.append("\t\t[%s] = {" % lua(c["name"] + "-" + c["realm"]))
        L += ["\t\t\t[%s] = %s," % (lua(k), lua(v)) for k, v in c.items()]
        L.append("\t\t},")
    L += ["\t},", '\t["collection"] = {', '\t\t["updated"] = %d,' % updated, '\t\t["journal"] = 1700,',
          '\t\t["collected"] = %d,' % len(collected),
          '\t\t["names"] = { %s },' % ", ".join(lua(m["name"]) for m in collected),
          '\t\t["ids"] = { %s },' % ", ".join(str(m["id"]) for m in collected)]
    got = {m["id"] for m in collected}
    L.append('\t\t["%s"] = {' % ("mounts" if v21 else "drops"))
    for m in journal:
        L.append('\t\t\t{ ["id"] = %d, ["name"] = %s, ["type"] = 1, ["from"] = %s%s },' % (
            m["id"], lua(m["name"]), lua("Источник: " + m["boss"]), ', ["got"] = true' if m["id"] in got else ""))
    L += ["\t\t},", "\t},", '\t["kills"] = {']
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


def column(rows, name):
    return [c for c, v in rows.get(0, {}).items() if v == name]


def row_of(rows, col, text):
    return [r for r, c in rows.items() if r > 0 and c.get(col) == text]


def mounts_on(book, title):
    rows, _ = cells(book, title)
    return [c.get(3) for r, c in sorted(rows.items()) if r > 0]


try:
    # ---------------------------------------------------------------- 2.0 makes the table
    check("2.0: signed in", login(OLD) == 0)
    # addon 2.0: the old table names B differently, so 2.0 does not find it in the collection
    write_sv([A, B], [A, B, C, D, E], NOW - 3600, v21=False)
    code, out = run(OLD, "--create-sheet", "Маунты WoW")
    check("2.0: table created", code == 0, out[-800:])
    book = list(state()["spreadsheets"].values())[0]
    sid = book["id"]
    rows, raid_sheet = cells(book, "Рейды")
    check("old layout: no ID column", not column(rows, ID), rows[0])
    check("old names in the old table: B under its old name", row_of(rows, 3, B["old"]) and not row_of(rows, 3, B["name"]))
    a_row = row_of(rows, 3, A["name"])[0]
    b_row = row_of(rows, 3, B["old"])[0]
    check("2.0 marked A (same name), missed B (renamed mount)", rows[a_row][0] == "Да" and rows[b_row][0] == "Нет")
    check("B is on the character sheets under the old name", B["old"] in mounts_on(book, "Рейды Эйра") and B["old"] in mounts_on(book, "Рейды Мирна"))
    width_before = raid_sheet["cols"]

    # ---------------------------------------------------------------- 2.1: sync with the collection
    write_sv([A, B, C], [A, B, C, D, E], NOW - 60)
    code, out = run(EXE, "--sync-collection")
    check("2.1 --sync-collection exits 0", code == 0, out[-1500:])
    book = state()["spreadsheets"][sid]
    rows, raid_sheet = cells(book, "Рейды")
    idc = column(rows, ID)
    check("hidden «ID в игре» column appended to «Рейды»", idc == [width_before] and idc[0] in raid_sheet.get("hidden", {}).get("COLUMNS", []),
          (idc, raid_sheet.get("hidden")))
    check("…and to «Подземелья»", column(cells(book, "Подземелья")[0], ID))
    check("IDs written for the catalog rows", rows[a_row].get(idc[0]) == A["id"] and rows[b_row].get(idc[0]) == B["id"],
          (rows[a_row], rows[b_row]))
    check("B renamed as in the game and «Да»", rows[b_row][3] == B["name"] and rows[b_row][0] == "Да", rows[b_row])
    if case_only:
        k = case_only[0]
        check("a name that differed only in case is also as in the game now", row_of(rows, 3, k[RC.index("Маунт")]), k[RC.index("Маунт")])
    c_row = row_of(rows, 3, C["name"])[0]
    check("C (collected since) «Да»; D, E still «Нет»", rows[c_row][0] == "Да"
          and rows[row_of(rows, 3, D["name"])[0]][0] == "Нет" and rows[row_of(rows, 3, E["name"])[0]][0] == "Нет")
    for title in ("Рейды Эйра", "Рейды Мирна"):
        on = mounts_on(book, title)
        check("«%s»: B and C gone (old or new name), D and E still there" % title,
              B["old"] not in on and B["name"] not in on and C["name"] not in on and D["name"] in on and E["name"] in on, on)
    check("log tells what changed", "Сверка с коллекцией: «Собрано?» = «Да»: 2" in out and "убрано строк с листов персонажей: 4" in out
          and "колонка «ID в игре»" in out, out[-1500:])
    meta = {m["metadataKey"]: m["metadataValue"] for m in book["devmeta"]}
    check("the collection snapshot is remembered in the table", meta.get("mountSheetSync.collectionChecked") == str(NOW - 60), meta)
    code, out2 = run(EXE, "--sync-collection")
    check("second sync: nothing left to do", code == 0 and "всё уже совпадает с коллекцией" in out2, out2[-600:])

    # ---------------------------------------------------------------- new mount from the game
    rec = {"id": "C-Эйра-Ревущий фьорд-%d" % D["id"], "time": NOW - 30, "char": "Эйра", "realm": "Ревущий фьорд",
           "source": "mount", "kind": "mount", "boss": D["name"], "mount": D["name"], "mountID": D["id"],
           "from": "Источник: " + D["boss"]}
    write_sv([A, B, C, D], [A, B, C, D, E], NOW - 60, kills=[rec])
    code, out = run(EXE, "--once")
    book = state()["spreadsheets"][sid]
    rows, _ = cells(book, "Рейды")
    check("new mount D: «Собрано?» = «Да»", code == 0 and rows[row_of(rows, 3, D["name"])[0]][0] == "Да", out[-1200:])
    check("…and D left the character sheets right away", D["name"] not in mounts_on(book, "Рейды Эйра") and D["name"] not in mounts_on(book, "Рейды Мирна"))

    # ---------------------------------------------------------------- «Да» typed by hand
    token = next(t for t, exp in state()["access"].items() if exp > time.time())
    e_row = row_of(rows, 3, E["name"])[0]
    api("/v4/spreadsheets/%s/values:batchUpdate" % sid, {"valueInputOption": "RAW", "data": [
        {"range": "'Рейды'!A%d" % (e_row + 1), "values": [["Да"]]}]}, token)
    code, out = run(EXE, "--test")
    book = state()["spreadsheets"][sid]
    check("hand-made «Да» on «Рейды»: E removed from the character sheets at the next contact",
          E["name"] not in mounts_on(book, "Рейды Эйра") and E["name"] not in mounts_on(book, "Рейды Мирна"), out[-800:])
    check("the rest of the character sheet is intact (header + other rows)", mounts_on(book, "Рейды Эйра")[:1] and
          cells(book, "Рейды Эйра")[0][0][0] == "Пытался на этой неделе?")

    # ---------------------------------------------------------------- the tray loop: a fresh collection snapshot
    write_sv([A, B, C, D, F], [A, B, C, D, E, F], NOW - 5)
    code, out = run(EXE, "--once")
    book = state()["spreadsheets"][sid]
    rows, _ = cells(book, "Рейды")
    check("sync loop: new snapshot → checked against the collection by itself",
          code == 0 and rows[row_of(rows, 3, F["name"])[0]][0] == "Да" and F["name"] not in mounts_on(book, "Рейды Эйра")
          and "Сверка с коллекцией: «Собрано?» = «Да»: 1" in out, out[-1200:])
    code, out = run(EXE, "--once")
    check("…and only once per snapshot", "Сверка с коллекцией" not in out, out[-800:])

    # ---------------------------------------------------------------- a new table by 2.1
    write_sv([A, B], [A, B, C, D, E], NOW - 10)
    code, out = run(EXE, "--create-sheet", "Маунты 2.1")
    check("2.1: new table created", code == 0, out[-800:])
    nb = [b for b in state()["spreadsheets"].values() if b["title"] == "Маунты 2.1"][0]
    rows, sh = cells(nb, "Рейды")
    idc = column(rows, ID)
    check("new table: «ID в игре» is the last, hidden column", idc and idc[0] == max(rows[0]) and idc[0] in sh.get("hidden", {}).get("COLUMNS", []))
    check("new table: names as in the game, «Собрано?» by ID", row_of(rows, 3, B["name"]) and not row_of(rows, 3, B["old"])
          and rows[row_of(rows, 3, B["name"])[0]][0] == "Да")
    check("new table: collected mounts are not on the character sheets", B["name"] not in mounts_on(nb, "Рейды Эйра")
          and A["name"] not in mounts_on(nb, "Рейды Эйра") and C["name"] in mounts_on(nb, "Рейды Эйра"))
    meta = {m["metadataKey"]: m["metadataValue"] for m in nb["devmeta"]}
    check("new table remembers the collection snapshot", meta.get("mountSheetSync.collectionChecked") == str(NOW - 10), meta)
finally:
    mock.terminate()

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
