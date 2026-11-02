"""The automatic weekly reset, the cases a game week really goes through: a new week with no kills yet, nothing to do,
a second computer with the same spreadsheet, Google failing during the reset and the retry.

  python3 e2e_week.py <MountSheetSync.exe>
"""
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
import paths

EXE = os.path.abspath(sys.argv[1])
HERE = os.path.dirname(os.path.abspath(__file__))
W = paths.work("e2e_week")
PORT = 18941
BASE = "http://127.0.0.1:%d" % PORT
NOW = int(time.time())
WEEK = 7 * 86400
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


shutil.rmtree(W, ignore_errors=True)
DATA = os.path.join(W, "data")
WOW = os.path.join(W, "WoW")
SV = os.path.join(WOW, "_retail_", "WTF", "Account", "ACC1", "SavedVariables")
ADDON = os.path.join(WOW, "_retail_", "Interface", "AddOns", "MountSheetSync")
for d in (DATA, SV, ADDON):
    os.makedirs(d)
open(os.path.join(ADDON, "MountSheetSync.toc"), "w").write("## Interface: 120105\n")
BROWSER = os.path.join(W, "browser.txt")
json.dump({"wowPath": WOW, "pollSeconds": 2}, open(os.path.join(DATA, "settings.json"), "w"))
ENV = dict(os.environ, MSS_GOOGLE_TEST=BASE, MSS_BROWSER_LOG=BROWSER, LANG="C.UTF-8", MSS_SYSTEM_LANG="ru", MSS_WOW_RUNNING="1")
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
    ENV.pop(k, None)
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
mock = subprocess.Popen(["python3", os.path.join(HERE, "mock_google.py"), str(PORT), os.path.join(W, "mock_state.json"), os.path.join(W, "mock.log")])
time.sleep(0.8)


def api(path, body):
    req = urllib.request.Request(BASE + path, data=json.dumps(body).encode("utf-8"), headers={"Content-Type": "application/json"}, method="POST")
    return json.loads(OPENER.open(req, timeout=20).read().decode("utf-8"))


def state():
    return json.loads(OPENER.open(BASE + "/__state", timeout=20).read().decode("utf-8"))


def run(*args, data=DATA, timeout=180):
    p = subprocess.run(["mono", EXE, "--data", data] + list(args), env=ENV, capture_output=True, timeout=timeout)
    return p.returncode, (p.stdout + p.stderr).decode("utf-8", "replace")


def login():
    proc = subprocess.Popen(["mono", EXE, "--data", DATA, "--google-login"], env=ENV, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    deadline = time.time() + 60
    while time.time() < deadline and proc.poll() is None:
        if os.path.exists(BROWSER) and open(BROWSER, encoding="utf-8").read().strip():
            try:
                OPENER.open(open(BROWSER, encoding="utf-8").read().strip().splitlines()[-1], timeout=20).read()
            except urllib.error.HTTPError:
                pass
            break
        time.sleep(0.2)
    proc.communicate(timeout=60)
    return proc.returncode


def lua(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return repr(v)
    if isinstance(v, str):
        return '"' + v.replace("\\", "\\\\").replace('"', '\\"') + '"'
    if isinstance(v, list):
        return "{\n" + "".join(lua(x) + ",\n" for x in v) + "}"
    return "{\n" + "".join("[%s] = %s,\n" % (lua(k), lua(x)) for k, x in v.items()) + "}"


CHARS = {"Эйра-Ревущий фьорд": {"name": "Эйра", "realm": "Ревущий фьорд", "class": "Охотница", "classFile": "HUNTER", "level": 90, "seen": NOW},
         "Мирна-Ревущий фьорд": {"name": "Мирна", "realm": "Ревущий фьорд", "class": "Жрица", "classFile": "PRIEST", "level": 90, "seen": NOW}}
COLLECTION = {"updated": NOW, "locale": "ruRU", "journal": 2, "collected": 0, "names": [], "ids": [], "mounts": []}


def write_sv(kills):
    open(os.path.join(SV, "MountSheetSync.lua"), "w", encoding="utf-8", newline="\r\n").write(
        "\nMountSheetSyncDB = " + lua({"version": 1, "chars": CHARS, "collection": COLLECTION, "kills": kills}) + "\n")


def sheet(book, title):
    return [s for s in book["sheets"] if s["title"] == title][0]


def cell(book, title, r, c):
    v = sheet(book, title)["cells"].get("%d,%d" % (r, c), {})
    return v.get("stringValue", v.get("numberValue", v.get("boolValue", v.get("formulaValue"))))


def find_row(book, title, col, text):
    for k, v in sheet(book, title)["cells"].items():
        r, c = map(int, k.split(","))
        if c == col and v.get("stringValue") == text:
            return r
    raise AssertionError("no %s on %s" % (text, title))


def attempts(book):
    """«Попыток» of Отмщение on the raids sheet."""
    s = sheet(book, "Рейды")
    col = [int(k.split(",")[1]) for k, v in s["cells"].items() if k.startswith("0,") and v.get("stringValue") == "Попыток"][0]
    return cell(book, "Рейды", find_row(book, "Рейды", 3, "Отмщение"), col)


def mark_last_week(book):
    """Last week's marks: «Да» on a character's sheet and a tick on «Мировые боссы»."""
    r = find_row(book, "Рейды Эйра", 3, "Отмщение")
    sheet(book, "Рейды Эйра")["cells"]["%d,0" % r] = {"stringValue": "Да"}
    w = find_row(book, "Мировые боссы", 3, "Сын Галеона")
    sheet(book, "Мировые боссы")["cells"]["%d,12" % w] = {"boolValue": True}
    api("/__control", {"setBook": book})
    return r, w


def reset_meta(book):
    return [m["metadataValue"] for m in book["devmeta"] if m["metadataKey"] == "mountSheetSync.lastWeekReset"]


def ledger(data=DATA):
    text = ""
    for f in os.listdir(data):
        if f.startswith("sent-ids-google"):
            text += open(os.path.join(data, f), encoding="utf-8").read()
    return text


def writes_since(n):
    return [p for m, p in state()["requests"][n:] if p.endswith("/values:batchUpdate")]


try:
    write_sv([])
    check("sign-in", login() == 0)
    code, out = run("--create-sheet")
    check("--create-sheet", code == 0, out[-800:])
    book = list(state()["spreadsheets"].values())[0]
    sid = book["id"]

    # ---------------------------------------------------------------- the journal says nothing about the week
    code, out = run("--once")
    book = state()["spreadsheets"][sid]
    check("empty journal: the week is unknown, no reset", code == 0 and not reset_meta(book) and "R-auto" not in ledger(), out[-500:])

    # ---------------------------------------------------------------- a new week has begun, no kills on it yet
    week_over = NOW - 3600   # the week of this record ended an hour ago
    new_week_end = week_over + WEEK
    r, w = mark_last_week(book)
    kills = [{"id": "K-old", "time": NOW - 2 * 86400, "char": "Эйра", "realm": "Ревущий фьорд", "charKey": "Player-1-A", "source": "encounter",
              "boss": "Сильвана Ветрокрылая", "kind": "raid", "difficultyID": 16, "weekEnd": week_over, "instance": "Святилище Господства"}]
    write_sv(kills)
    code, out = run("--once")
    book = state()["spreadsheets"][sid]
    check("new week, no kills on it: last week's «Да» cleared", code == 0 and cell(book, "Рейды Эйра", r, 0) == "Нет", (cell(book, "Рейды Эйра", r, 0), out[-800:]))
    check("…the tick on «Мировые боссы» too", cell(book, "Мировые боссы", w, 12) is False, cell(book, "Мировые боссы", w, 12))
    check("…the week is counted on from the old record", "R-auto-%d" % new_week_end in ledger(), ledger()[-200:])
    check("…the last week's kill is not marked again", cell(book, "Рейды Эйра", r, 0) == "Нет")
    check("…the time of the reset is stored", len(reset_meta(book)) == 1 and int(reset_meta(book)[0]) >= NOW - 60, reset_meta(book))
    check("…the list is rewritten after the reset", "resetAt" in open(os.path.join(ADDON, "SheetData.lua"), encoding="utf-8").read())
    first_reset = reset_meta(book)[0]

    # ---------------------------------------------------------------- the same week again
    n = len(state()["requests"])
    time.sleep(1.1)
    code, out = run("--once")
    book = state()["spreadsheets"][sid]
    check("next run on the same week: no second reset", code == 0 and reset_meta(book)[0] == first_reset and not writes_since(n), writes_since(n))

    # ---------------------------------------------------------------- a second computer with the same spreadsheet
    r, w = mark_last_week(book)
    data2 = os.path.join(W, "data2")
    shutil.copytree(DATA, data2, ignore=shutil.ignore_patterns("sent-ids*", "state.json", "sync.log"))
    time.sleep(1.1)
    code, out = run("--once", data=data2)
    book = state()["spreadsheets"][sid]
    check("second computer, the sheet was reset this week: no reset from it", code == 0 and "R-auto" not in ledger(data2)
          and reset_meta(book)[0] == first_reset and cell(book, "Рейды Эйра", r, 0) == "Да", (ledger(data2)[-200:], reset_meta(book), out[-600:]))

    # ---------------------------------------------------------------- Google fails during the reset
    next_end = NOW + 10 * 86400   # the journal now knows a later week; the reset above is before its start
    kills[0]["weekEnd"] = next_end
    write_sv(kills)
    api("/__control", {"failWrites": 5})
    code, out = run("--once", timeout=240)
    book = state()["spreadsheets"][sid]
    check("Google fails while resetting: said so, no crash", code == 0 and "сброс недели не прошёл" in out, out[-800:])
    check("…not remembered as done", "R-auto-%d" % next_end not in ledger())
    check("…the marks and the stored time are as they were", cell(book, "Рейды Эйра", r, 0) == "Да" and reset_meta(book)[0] == first_reset)
    api("/__control", {"failWrites": 0})
    attempts_before = attempts(book)
    code, out = run("--once")
    book = state()["spreadsheets"][sid]
    check("next run: the reset goes through", code == 0 and "R-auto-%d" % next_end in ledger() and cell(book, "Мировые боссы", w, 12) is False
          and reset_meta(book)[0] != first_reset, (cell(book, "Мировые боссы", w, 12), out[-800:]))
    check("…the kill of this week is marked again, the same as after the button", cell(book, "Рейды Эйра", r, 0) == "Да", cell(book, "Рейды Эйра", r, 0))
    check("…its attempt is not counted twice", attempts(book) == attempts_before and attempts_before >= 1, (attempts_before, attempts(book)))
finally:
    mock.terminate()

failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
