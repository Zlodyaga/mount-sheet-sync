"""End-to-end: MountSheetSync.exe 2.0 (under mono) against mock_google.py.

Sign-in through the browser hand-off (loopback + PKCE), sheet creation from SavedVariables (characters +
collection), kill / mount / reset sync through the Sheets API, token refresh and revocation, retries,
adding characters later, refused consent.

  python3 e2e_google.py <MountSheetSync.exe>
"""
import json
import re
import os
import shutil
import subprocess
import sys
import threading
import time
import urllib.request
import urllib.error
import urllib.parse
import paths

EXE = os.path.abspath(sys.argv[1])
HERE = os.path.dirname(os.path.abspath(__file__))
W = os.path.join(HERE, "..", "..", "..", "e2e_google_run")
W = paths.work("e2e_google")
PORT = 18931
BASE = "http://127.0.0.1:%d" % PORT
NOW = int(time.time())
WEEK_END = NOW + 3 * 86400

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
BROWSER_LOG = os.path.join(W, "browser.txt")
json.dump({"wowPath": WOW, "pollSeconds": 2}, open(os.path.join(DATA, "settings.json"), "w"))
# как будто K-1 уже уходил в старую таблицу со скриптом: в новую таблицу он всё равно должен попасть
OLD_LEDGER = "K-1\t%d\r\n" % int(time.time())
open(os.path.join(DATA, "sent-ids.txt"), "w", newline="").write(OLD_LEDGER)

mock = subprocess.Popen(["python3", os.path.join(HERE, "mock_google.py"), str(PORT), os.path.join(W, "mock_state.json"),
                         os.path.join(W, "mock.log")])
time.sleep(0.8)

ENV = dict(os.environ, MSS_GOOGLE_TEST=BASE, MSS_BROWSER_LOG=BROWSER_LOG, LANG="C.UTF-8", MSS_SYSTEM_LANG="ru")
for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
    ENV.pop(k, None)


def api(path, body=None):
    req = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(),
                                 method="GET" if body is None else "POST", headers={"Content-Type": "application/json"})
    return json.loads(urllib.request.urlopen(req, timeout=10).read())


def state():
    return api("/__state")


def run(*args, timeout=120):
    p = subprocess.run(["mono", EXE, "--data", DATA] + list(args), env=ENV, capture_output=True, timeout=timeout)
    return p.returncode, (p.stdout + p.stderr).decode("utf-8", "replace")


def login(expect_browser=True, retry=None):
    """Run --google-login; play the browser: open the logged URL, follow Google's redirect to 127.0.0.1.
    retry: called after the first page; then the page's «Попробовать ещё раз» link is followed (pages: both)."""
    if os.path.exists(BROWSER_LOG):
        os.remove(BROWSER_LOG)
    proc = subprocess.Popen(["mono", EXE, "--data", DATA, "--google-login"], env=ENV, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    page = None
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def get(u):
        try:
            return opener.open(u, timeout=20).read().decode("utf-8")
        except urllib.error.HTTPError as e:
            return "HTTP %d" % e.code
    deadline = time.time() + 60
    while time.time() < deadline and proc.poll() is None:
        if os.path.exists(BROWSER_LOG) and open(BROWSER_LOG, encoding="utf-8").read().strip():
            url = open(BROWSER_LOG, encoding="utf-8").read().strip().splitlines()[-1]
            page = get(url)
            if retry is not None:
                retry()
                m = re.search(r'href="(/retry)"', page)
                redirect = urllib.parse.parse_qs(urllib.parse.urlparse(url).query)["redirect_uri"][0]
                page = [page, get(redirect.rstrip("/") + m.group(1)) if m else None]
            break
        time.sleep(0.2)
    out = proc.communicate(timeout=60)[0].decode("utf-8", "replace")
    return proc.returncode, out, page


def sheet_cells(book, title):
    s = [x for x in book["sheets"] if x["title"] == title][0]
    rows = {}
    for k, v in s["cells"].items():
        r, c = map(int, k.split(","))
        val = v.get("stringValue", v.get("numberValue", v.get("boolValue", v.get("formulaValue"))))
        rows.setdefault(r, {})[c] = val
    return rows


def find_row(rows, col, text):
    for r, cells in rows.items():
        if cells.get(col) == text:
            return r
    return None


def lua_str(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") + '"'


def write_sv(chars, collected, kills):
    lines = ["", "MountSheetSyncDB = {", '\t["version"] = 1,', '\t["chars"] = {']
    for c in chars:
        lines.append('\t\t[%s] = {' % lua_str(c["name"] + "-" + c["realm"]))
        for k, v in c.items():
            lines.append('\t\t\t[%s] = %s,' % (lua_str(k), lua_str(v) if isinstance(v, str) else v))
        lines.append("\t\t},")
    lines.append("\t},")
    lines.append('\t["collection"] = {')
    lines.append('\t\t["updated"] = %d,' % NOW)
    lines.append('\t\t["journal"] = 1500,')
    lines.append('\t\t["collected"] = %d,' % len(collected))
    lines.append('\t\t["names"] = {')
    for i, n in enumerate(collected, 1):
        lines.append('\t\t\t%s, -- [%d]' % (lua_str(n), i))
    lines.append("\t\t},")
    lines.append("\t},")
    lines.append('\t["kills"] = {')
    for i, k in enumerate(kills, 1):
        lines.append("\t\t{")
        for key, v in k.items():
            lines.append('\t\t\t[%s] = %s,' % (lua_str(key), lua_str(v) if isinstance(v, str) else v))
        lines.append("\t\t}, -- [%d]" % i)
    lines.append("\t},")
    lines.append("}")
    open(os.path.join(SV_DIR, "MountSheetSync.lua"), "w", encoding="utf-8", newline="\r\n").write("\n".join(lines) + "\n")


EIRA = {"name": "Эйра", "realm": "Ревущий фьорд", "class": "Охотница", "classFile": "HUNTER", "level": 90, "seen": NOW - 100}
MIRNA = {"name": "Мирна", "realm": "Ревущий фьорд", "class": "Жрица", "classFile": "PRIEST", "level": 90, "seen": NOW - 50}
kills = []
write_sv([EIRA, MIRNA], ["Пепел Ал'ара", "Синий протодракон"], kills)

try:
    # ---------------------------------------------------------------- sign-in
    code, out, page = login()
    check("--google-login exits 0", code == 0, out[-800:])
    check("the browser page says sign-in is done", page and "Вход выполнен" in page, page)
    check("log names the account", "Вход в Google выполнен: tester@example.com" in out, out[-500:])
    tok = os.path.join(DATA, "google-token.dat")
    check("refresh token stored, not in plain text", os.path.exists(tok) and b"refresh" not in open(tok, "rb").read()
          and b"1//refresh" not in open(tok, "rb").read())
    st = json.load(open(os.path.join(DATA, "settings.json"), encoding="utf-8-sig"))
    check("settings switched to Google mode", st.get("mode") == "google", st)
    url = open(BROWSER_LOG, encoding="utf-8").read().strip()
    check("auth URL: PKCE S256, offline, drive.file scope only for files", "code_challenge_method=S256" in url
          and "access_type=offline" in url and "drive.file" in url and "spreadsheets" not in url.split("scope=")[1].split("&")[0], url)

    # ---------------------------------------------------------------- create the sheet
    code, out = run("--create-sheet", "Маунты (тест)")
    check("--create-sheet exits 0", code == 0, out[-1500:])
    s = state()
    books = list(s["spreadsheets"].values())
    check("one spreadsheet created", len(books) == 1, len(books))
    book = books[0]
    titles = [x["title"] for x in book["sheets"]]
    check("sheets: masters, per-character, summary, world bosses",
          titles == ["Рейды", "Рейды Мирна", "Рейды Эйра", "Подземелья", "Подземелья Мирна", "Подземелья Эйра", "Сводка", "Мировые боссы"], titles)
    check("title and Russian locale", book["title"] == "Маунты (тест)" and book["locale"] == "ru_RU", (book["title"], book["locale"]))
    raids = sheet_cells(book, "Рейды")
    r = find_row(raids, 3, "Пепел Ал'ара")
    check("collected mount: «Собрано?» = Да", r is not None and raids[r][0] == "Да", raids.get(r))
    r2 = find_row(raids, 3, "Отмщение")
    check("not collected: «Собрано?» = Нет", r2 is not None and raids[r2][0] == "Нет")
    dung = sheet_cells(book, "Подземелья")
    r3 = find_row(dung, 3, "Синий протодракон")
    check("names as in the game: «Синий протодракон» (was «Поводья синего протодракона»), «Да»",
          r3 is not None and dung[r3][0] == "Да" and find_row(dung, 3, "Поводья синего протодракона") is None, dung.get(r3))
    check("hidden «ID в игре» column with the mount IDs", raids[0].get(15) == "ID в игре" and raids[find_row(raids, 3, "Пепел Ал'ара")].get(15) == 183
          and 15 in [x for x in book["sheets"] if x["title"] == "Рейды"][0].get("hidden", {}).get("COLUMNS", []), raids[0])
    eira = sheet_cells(book, "Рейды Эйра")
    check("character sheet: header and «Нет» rows", eira[0][0] == "Пытался на этой неделе?" and all(v[0] == "Нет" for k, v in eira.items() if k > 0) and len(eira) > 10, len(eira))
    check("character sheet skips collected mounts", find_row(eira, 3, "Пепел Ал'ара") is None and find_row(eira, 3, "Отмщение") is not None)
    world = sheet_cells(book, "Мировые боссы")
    check("world bosses: a checkbox column per character", world[0].get(10) == "Мирна" and world[0].get(11) == "Эйра" and world[1].get(10) is False)
    check("raid «left» formula counts both characters", "'Рейды Мирна'" in raids[1][8] and "'Рейды Эйра'" in raids[1][8], raids[1].get(8))
    check("program marker in developer metadata", any(m["metadataKey"] == "mountSheetSync" for m in book["devmeta"]))
    st = json.load(open(os.path.join(DATA, "settings.json"), encoding="utf-8-sig"))
    check("settings remember the new sheet", st.get("spreadsheetId") == book["id"] and st.get("spreadsheetTitle") == "Маунты (тест)", st)

    # ---------------------------------------------------------------- kills through the Sheets API
    kills.append({"id": "K-1", "time": NOW - 30, "char": "Эйра", "realm": "Ревущий фьорд", "charKey": "Player-1-A", "source": "encounter",
                  "boss": "Сильвана Ветрокрылая", "encounterID": 2435, "instance": "Святилище Господства", "kind": "raid",
                  "difficultyID": 16, "difficulty": "Эпохальный", "weekEnd": WEEK_END})
    kills.append({"id": "C-Player-1-A-1587", "time": NOW - 20, "char": "Эйра", "realm": "Ревущий фьорд", "charKey": "Player-1-A",
                  "source": "mount", "kind": "mount", "boss": "Зеретский смотритель", "mount": "Зеретский смотритель", "mountID": 1587,
                  "from": "Добыча: Тюремщик / Рейд: Гробница Предвечных"})
    write_sv([EIRA, MIRNA], ["Пепел Ал'ара", "Синий протодракон"], kills)
    code, out = run("--once")
    check("--once exits 0", code == 0, out[-1500:])
    book = state()["spreadsheets"][book["id"]]
    eira = sheet_cells(book, "Рейды Эйра")
    r = find_row(eira, 3, "Отмщение")
    check("kill marks «Да» on Эйра' sheet (sent to the old sheet before — the new one has its own list)", eira[r][0] == "Да", eira.get(r))
    ledgers = sorted(f for f in os.listdir(DATA) if f.startswith("sent-ids"))
    check("separate sent list for the new sheet; the old one untouched", ledgers == ["sent-ids-google-%s.txt" % book["id"], "sent-ids.txt"]
          and open(os.path.join(DATA, "sent-ids.txt"), newline="").read() == OLD_LEDGER, ledgers)
    mim = sheet_cells(book, "Рейды Мирна")
    check("…not on Мирна's", mim[find_row(mim, 3, "Отмщение")][0] == "Нет")
    raids = sheet_cells(book, "Рейды")
    r = find_row(raids, 3, "Зеретский смотритель")
    check("new mount: «Собрано?» = Да on the master sheet", r is not None and raids[r][0] == "Да", raids.get(r))
    sheetdata = open(os.path.join(ADDON, "SheetData.lua"), encoding="utf-8").read()
    check("SheetData.lua for the in-game list written from the new sheet", "MountSheetSyncSheet" in sheetdata and "Маунты (тест)" in sheetdata)
    reqs = state()["requests"]
    writes = [p for m, p in reqs if p.endswith("/values:batchUpdate")]
    check("writes go as values:batchUpdate", len(writes) >= 1, writes)

    # ---------------------------------------------------------------- nothing new -> no writes
    n_before = len(state()["requests"])
    code, out = run("--once")
    after = state()["requests"][n_before:]
    check("second run: no new records, no writes", code == 0 and not any(p.endswith("values:batchUpdate") for m, p in after), after)

    # ---------------------------------------------------------------- week reset -> property, then again (update)
    kills.append({"id": "R-1", "time": NOW - 5, "char": "Эйра", "realm": "Ревущий фьорд", "charKey": "Player-1-A", "kind": "reset",
                  "boss": "Сброс недели", "weekEnd": WEEK_END})
    write_sv([EIRA, MIRNA], ["Пепел Ал'ара", "Синий протодракон"], kills)
    code, out = run("--once")
    book = state()["spreadsheets"][book["id"]]
    meta = [m for m in book["devmeta"] if m["metadataKey"] == "mountSheetSync.lastWeekReset"]
    check("reset: time stored in developer metadata", code == 0 and len(meta) == 1 and int(meta[0]["metadataValue"]) >= NOW - 60, (meta, out[-800:]))
    eira = sheet_cells(book, "Рейды Эйра")
    check("reset then this week's kill again: Отмщение still «Да»", eira[find_row(eira, 3, "Отмщение")][0] == "Да")
    kills.append({"id": "R-2", "time": NOW - 2, "char": "Мирна", "realm": "Ревущий фьорд", "charKey": "Player-1-B", "kind": "reset",
                  "boss": "Сброс недели", "weekEnd": WEEK_END})
    write_sv([EIRA, MIRNA], ["Пепел Ал'ара", "Синий протодракон"], kills)
    first = meta[0]["metadataValue"]
    time.sleep(1.1)
    code, out = run("--once")
    book = state()["spreadsheets"][book["id"]]
    meta = [m for m in book["devmeta"] if m["metadataKey"] == "mountSheetSync.lastWeekReset"]
    check("second reset updates the same metadata entry", len(meta) == 1 and meta[0]["metadataValue"] != first, meta)

    # ---------------------------------------------------------------- expired access token, Google busy
    api("/__control", {"expireAccess": True, "fail": [503, 429]})
    kills.append({"id": "K-2", "time": NOW, "char": "Мирна", "realm": "Ревущий фьорд", "charKey": "Player-1-B", "source": "encounter",
                  "boss": "Сильвана Ветрокрылая", "kind": "raid", "difficultyID": 16, "weekEnd": WEEK_END, "instance": "Святилище Господства"})
    write_sv([EIRA, MIRNA], ["Пепел Ал'ара", "Синий протодракон"], kills)
    code, out = run("--once")
    book = state()["spreadsheets"][book["id"]]
    mim = sheet_cells(book, "Рейды Мирна")
    check("expired access token refreshed, 503/429 retried, kill recorded", code == 0 and mim[find_row(mim, 3, "Отмщение")][0] == "Да", out[-1200:])
    check("retries are logged", "Google ответил 503" in out and "Google ответил 429" in out, out[-1200:])

    # ---------------------------------------------------------------- add a character later
    LOK = {"name": "Каэлия", "realm": "Гордунни", "class": "Чернокнижница", "classFile": "WARLOCK", "level": 80, "seen": NOW}
    write_sv([EIRA, MIRNA, LOK], ["Пепел Ал'ара", "Синий протодракон"], kills)
    code, out = run("--add-chars")
    book = state()["spreadsheets"][book["id"]]
    titles = [x["title"] for x in book["sheets"]]
    check("--add-chars: new sheets next to the others", code == 0 and titles == ["Рейды", "Рейды Мирна", "Рейды Эйра", "Рейды Каэлия", "Подземелья",
          "Подземелья Мирна", "Подземелья Эйра", "Подземелья Каэлия", "Сводка", "Мировые боссы"], (titles, out[-1000:]))
    lok = sheet_cells(book, "Рейды Каэлия")
    check("new character's rows follow «Собрано?» now (Зеретский смотритель is collected)",
          find_row(lok, 3, "Зеретский смотритель") is None and find_row(lok, 3, "Отмщение") is not None and len(lok) > 10)
    raids = sheet_cells(book, "Рейды")
    check("raid «left» formula now has three characters", raids[1][8].count("COUNTIFS") == 3 and "'Рейды Каэлия'" in raids[1][8], raids[1][8])
    world = sheet_cells(book, "Мировые боссы")
    check("world bosses: new checkbox column", world[0].get(12) == "Каэлия" and world[1].get(12) is False, world[0])
    check("world «left» formula covers three columns", "K2:K" in world[1][6] and "M2:M" in world[1][6], world[1][6])
    code, out = run("--add-chars")
    check("--add-chars again: nothing to add", code == 0 and "Все персонажи уже есть" in out, out[-500:])

    # ---------------------------------------------------------------- revoked sign-in
    api("/__control", {"expireAccess": True, "revokeRefresh": True})
    kills.append({"id": "K-3", "time": NOW + 1, "char": "Эйра", "realm": "Ревущий фьорд", "charKey": "Player-1-A", "source": "bosskill",
                  "boss": "Галлеон", "kind": "world", "weekEnd": WEEK_END})
    write_sv([EIRA, MIRNA, LOK], ["Пепел Ал'ара", "Синий протодракон"], kills)
    code, out = run("--once")
    check("revoked sign-in: explained, token forgotten", "вход в Google больше не действует" in out and not os.path.exists(tok), out[-1000:])

    # ---------------------------------------------------------------- refused consent / missing scope
    api("/__control", {"consent": "deny"})
    code, out, page = login()
    check("consent refused: exit 1 with a reason", code == 1 and "вход отменён" in out and page and "не выполнен" in page, (out[-500:], page))
    api("/__control", {"consent": "noscope"})
    code, out, page = login(retry=lambda: api("/__control", {"consent": "ok"}))
    check("file access unticked: the browser page explains and offers a retry",
          page[0] and "не дал программе доступ к файлам" in page[0] and "Попробовать ещё раз" in page[0]
          and "Вход выполнен" not in page[0], page)
    check("«Попробовать ещё раз» opens Google again and finishes the sign-in",
          code == 0 and page[1] and "Вход выполнен" in page[1] and os.path.exists(tok), (out[-800:], page))
    check("the log tells about the missing tick", "нет доступа к файлам" in out, out[-800:])
    code, out = run("--once")
    book = state()["spreadsheets"][book["id"]]
    world = sheet_cells(book, "Мировые боссы")
    r = find_row(world, 4, "Галлеон")
    check("after signing in again the pending world boss kill is sent (Эйра' checkbox)", code == 0 and r is not None
          and world[r].get(11) is True and world[r].get(10) is False, (out[-800:], world.get(r)))
finally:
    mock.terminate()

passed = sum(1 for _, ok in checks if ok)
print("\n%d/%d checks passed" % (passed, len(checks)))
sys.exit(0 if passed == len(checks) else 1)
