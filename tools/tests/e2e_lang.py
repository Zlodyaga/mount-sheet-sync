"""End-to-end: spreadsheets in other languages (MountSheetSync.exe under mono against mock_google.py).

A German spreadsheet from a German game (mount names from the mount journal, bosses and instances from the
Encounter Journal dump of addon 2.4) and an English one from a Russian game (names from the program's own English
texts): sheet names, headers, Yes/No, statuses, drop-down lists, filters, formulas (';' for de_DE), not a single
Russian word in them; kills recognised by encounter ID whatever the game calls the boss; week reset, adding a
character, the in-game "Remaining" list.

  python3 e2e_lang.py <MountSheetSync.exe>
"""
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.request
import urllib.error
import urllib.parse
import paths

EXE = os.path.abspath(sys.argv[1])
HERE = os.path.dirname(os.path.abspath(__file__))
W = paths.work("e2e_lang")
PORT = 18937
BASE = "http://127.0.0.1:%d" % PORT
NOW = int(time.time())
WEEK_END = NOW + 3 * 86400
CYR = re.compile("[А-Яа-яЁё]")

checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


shutil.rmtree(W, ignore_errors=True)
os.makedirs(W)
mock = subprocess.Popen(["python3", os.path.join(HERE, "mock_google.py"), str(PORT), os.path.join(W, "mock_state.json"),
                         os.path.join(W, "mock.log")])
time.sleep(0.8)


def api(path, body=None):
    req = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(),
                                 method="GET" if body is None else "POST", headers={"Content-Type": "application/json"})
    return json.loads(urllib.request.urlopen(req, timeout=10).read())


def state():
    return api("/__state")


class Setup:
    """A data folder, a WoW folder with the addon's SavedVariables, sign-in."""

    def __init__(self, name, ui_lang):
        self.dir = os.path.join(W, name)
        self.data = os.path.join(self.dir, "data")
        wow = os.path.join(self.dir, "WoW")
        self.sv = os.path.join(wow, "_retail_", "WTF", "Account", "ACC1", "SavedVariables")
        self.addon = os.path.join(wow, "_retail_", "Interface", "AddOns", "MountSheetSync")
        for d in (self.data, self.sv, self.addon):
            os.makedirs(d)
        open(os.path.join(self.addon, "MountSheetSync.toc"), "w").write("## Interface: 120105\n")
        self.browser = os.path.join(self.dir, "browser.txt")
        json.dump({"wowPath": wow, "pollSeconds": 2}, open(os.path.join(self.data, "settings.json"), "w"))
        self.env = dict(os.environ, MSS_GOOGLE_TEST=BASE, MSS_BROWSER_LOG=self.browser, LANG="C.UTF-8", MSS_SYSTEM_LANG=ui_lang)
        for k in ("http_proxy", "https_proxy", "HTTP_PROXY", "HTTPS_PROXY", "no_proxy", "NO_PROXY"):
            self.env.pop(k, None)

    def run(self, *args, timeout=180):
        p = subprocess.run(["mono", EXE, "--data", self.data] + list(args), env=self.env, capture_output=True, timeout=timeout)
        return p.returncode, (p.stdout + p.stderr).decode("utf-8", "replace")

    def login(self):
        proc = subprocess.Popen(["mono", EXE, "--data", self.data, "--google-login"], env=self.env, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT)
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        deadline = time.time() + 60
        while time.time() < deadline and proc.poll() is None:
            if os.path.exists(self.browser) and open(self.browser, encoding="utf-8").read().strip():
                url = open(self.browser, encoding="utf-8").read().strip().splitlines()[-1]
                try:
                    opener.open(url, timeout=20).read()
                except urllib.error.HTTPError:
                    pass
                break
            time.sleep(0.2)
        proc.communicate(timeout=60)
        return proc.returncode

    def write_sv(self, db):
        open(os.path.join(self.sv, "MountSheetSync.lua"), "w", encoding="utf-8", newline="\r\n").write(
            "\nMountSheetSyncDB = " + lua(db, 0) + "\n")

    def sheet_data(self):
        p = os.path.join(self.addon, "SheetData.lua")
        return open(p, encoding="utf-8").read() if os.path.exists(p) else ""


def lua(v, depth):
    pad = "\t" * (depth + 1)
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return repr(v)
    if isinstance(v, str):
        return '"' + v.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") + '"'
    if isinstance(v, list):
        return "{\n" + "".join("%s%s, -- [%d]\n" % (pad, lua(x, depth + 1), i) for i, x in enumerate(v, 1)) + "\t" * depth + "}"
    return "{\n" + "".join("%s[%s] = %s,\n" % (pad, lua(k, 0), lua(x, depth + 1)) for k, x in v.items()) + "\t" * depth + "}"


def sheet_cells(book, title):
    s = [x for x in book["sheets"] if x["title"] == title][0]
    rows = {}
    for k, v in s["cells"].items():
        r, c = map(int, k.split(","))
        rows.setdefault(r, {})[c] = v.get("stringValue", v.get("numberValue", v.get("boolValue", v.get("formulaValue"))))
    return rows


def sheet_id(book, title):
    return [x for x in book["sheets"] if x["title"] == title][0]["sheetId"]


def find_row(rows, col, text):
    for r, cells in rows.items():
        if cells.get(col) == text:
            return r
    return None


def all_texts(book):
    """Every text the spreadsheet shows: cells, sheet names, lists, filters, conditional formulas."""
    out = [("title", s["title"]) for s in book["sheets"]]
    for s in book["sheets"]:
        for k, v in s["cells"].items():
            t = v.get("stringValue", v.get("formulaValue"))
            if isinstance(t, str):
                out.append((s["title"] + "!" + k, t))
    for x in book["validations"]:
        for v in x["rule"]["condition"].get("values", []):
            out.append(("validation", v.get("userEnteredValue", "")))
    for x in book["filters"]:
        for spec in x["filter"].get("filterSpecs", []):
            for h in spec.get("filterCriteria", {}).get("hiddenValues", []):
                out.append(("filter", h))
    for x in book["formats"]:
        for v in x.get("rule", {}).get("booleanRule", {}).get("condition", {}).get("values", []):
            out.append(("format", v.get("userEnteredValue", "")))
    return out


def kill(kid, char, realm, boss, enc, instance, diff=16, kind="raid"):
    return {"id": kid, "time": NOW - 30, "char": char, "realm": realm, "charKey": "Player-" + char, "source": "encounter",
            "boss": boss, "encounterID": enc, "instance": instance, "kind": kind, "difficultyID": diff, "weekEnd": WEEK_END}


def char(name, realm):
    return {"name": name, "realm": realm, "class": "Jägerin", "classFile": "HUNTER", "level": 90, "seen": NOW - 100}


try:
    # ================================================================ German game → German spreadsheet
    de = Setup("de", "de")
    EIRA, MIRNA = char("Eira", "Blackhand"), char("Mirna", "Blackhand")
    collection = {"updated": NOW, "locale": "deDE", "journal": 1500, "collected": 2,
                  "names": ["Asche von Al'ar", "Blauer Protodrache"], "ids": [183, 264],
                  "mounts": [{"id": 183, "name": "Asche von Al'ar", "type": 1, "got": True},
                             {"id": 1471, "name": "Rache", "type": 1},
                             {"id": 415, "name": "Reinblütiger Feuerfalke", "type": 1},
                             {"id": 264, "name": "Blauer Protodrache", "type": 1, "got": True}]}
    journal = {"locale": "deDE", "build": "66192", "updated": NOW,
               "instances": [{"id": 1193, "name": "Sanktum der Herrschaft", "raid": True, "map": 2450},
                             {"id": 78, "name": "Feuerlande", "raid": True, "map": 720}],
               "encounters": [{"id": 2435, "j": 2441, "name": "Sylvanas Windläufer", "i": 1193},
                              {"id": 1203, "j": 198, "name": "Ragnaros", "i": 78}]}
    kills = []
    db = {"version": 1, "chars": {"Eira-Blackhand": EIRA, "Mirna-Blackhand": MIRNA}, "collection": collection,
          "journal": journal, "kills": kills}
    de.write_sv(db)
    check("de: sign-in", de.login() == 0)
    code, out = de.run("--create-sheet")   # language: the game's (deDE), title: the default one
    check("de: --create-sheet exits 0", code == 0, out[-1500:])
    book = [b for b in state()["spreadsheets"].values()][0]
    titles = [x["title"] for x in book["sheets"]]
    check("de: German sheet names", titles == ["Schlachtzüge", "Schlachtzüge Eira", "Schlachtzüge Mirna", "Dungeons", "Dungeons Eira",
          "Dungeons Mirna", "Übersicht", "Weltbosse"], titles)
    check("de: German title and locale", book["title"] == "WoW-Reittiere" and book["locale"] == "de_DE", (book["title"], book["locale"]))
    mark = [m for m in book["devmeta"] if m["metadataKey"] == "mountSheetSync"]
    check("de: the program's marker names the language", mark and json.loads(mark[0]["metadataValue"]).get("lang") == "de", mark)
    raids = sheet_cells(book, "Schlachtzüge")
    check("de: German headers", raids[0][0] == "Gesammelt?" and raids[0][3] == "Reittier" and raids[0][12] == "Status"
          and raids[0][17] == "Spiel-ID", raids[0])
    r = find_row(raids, 17, 1471)
    check("de: mount, boss and raid named as in the German game", r is not None and raids[r][3] == "Rache"
          and raids[r][5] == "Sylvanas Windläufer" and raids[r][2] == "Sanktum der Herrschaft", raids.get(r))
    check("de: «Gesammelt?» = Nein, status Farmbar", raids[r][0] == "Nein" and raids[r][12] == "Farmbar", raids.get(r))
    r = find_row(raids, 17, 183)
    check("de: collected mount «Ja»", r is not None and raids[r][0] == "Ja" and raids[r][3] == "Asche von Al'ar", raids.get(r))
    r = find_row(raids, 17, 117)
    check("de: a mount the German journal did not list — English name; texts English",
          r is not None and raids[r][3] == "Blue Qiraji Battle Tank" and raids[r][5] == "Regular NPCs inside AQ40", raids.get(r))
    r = find_row(raids, 17, 1554) or find_row(raids, 17, 1552)
    check("de: removed reward — «Nicht verfügbar» status, reset «Nicht erhältlich»",
          r is not None and raids[r][12] == "Nicht verfügbar" and raids[r][7] == "Nicht erhältlich", raids.get(r))
    left = raids[1][8]
    check("de: «left» formula: German sheet names, «Nein»", "'Schlachtzüge Eira'" in left and '"Nein"' in left
          and "Нет" not in left, left)
    summary = sheet_cells(book, "Übersicht")
    check("de: summary in German, formulas on the German sheets", summary[0][0].startswith("WoW Retail / Midnight — Reittiere")
          and "'Schlachtzüge'!" in summary[5][1] and '"Nicht verfügbar*"' in summary[5][4] and summary[4][0] == "Blatt",
          [summary.get(0), summary.get(4), summary.get(5)])
    sid = sheet_id(book, "Schlachtzüge")
    lists = [[v["userEnteredValue"] for v in x["rule"]["condition"]["values"]] for x in book["validations"]
             if x["range"]["sheetId"] == sid and x["rule"]["condition"]["type"] == "ONE_OF_LIST"]
    check("de: drop-down lists in German", ["Nein", "Ja"] in lists and any("Farmbar" in l and "Nicht verfügbar" in l for l in lists), lists)
    hidden = [h for x in book["filters"] if x["filter"]["range"]["sheetId"] == sid
              for spec in x["filter"]["filterSpecs"] for h in spec["filterCriteria"].get("hiddenValues", [])]
    check("de: filter hides «Ja» and the unavailable statuses", "Ja" in hidden and "Nicht verfügbar" in hidden, hidden)
    russian = [(w, t) for w, t in all_texts(book) if CYR.search(t)]
    check("de: not a single Russian word in the spreadsheet", not russian, russian[:20])
    eira = sheet_cells(book, "Schlachtzüge Eira")
    check("de: character sheet header and «Nein» rows", eira[0][0] == "Diese Woche versucht?" and eira[0][7] == "Farme ich"
          and all(v[0] == "Nein" for k, v in eira.items() if k > 0) and len(eira) > 10, eira.get(0))

    # ---------------------------------------------------------------- kills
    kills.append(kill("K-1", "Eira", "Blackhand", "Sylvanas Windläufer", 2435, "Sanktum der Herrschaft"))
    # the same boss reported by a Russian client: only the encounter ID matches
    kills.append(kill("K-2", "Mirna", "Blackhand", "Рагнарос", 1203, "Огненные Просторы", diff=14))
    kills[-1]["weekEnd"] = WEEK_END - 60   # not this reset's week: not marked again after the reset below
    kills.append({"id": "C-1", "time": NOW - 20, "char": "Eira", "realm": "Blackhand", "charKey": "Player-Eira", "source": "mount",
                  "kind": "mount", "boss": "Rache", "mount": "Rache", "mountID": 1471, "from": "Beute: Sylvanas Windläufer"})
    de.write_sv(db)
    code, out = de.run("--once")
    check("de: --once exits 0", code == 0, out[-1500:])
    book = state()["spreadsheets"][book["id"]]
    eira = sheet_cells(book, "Schlachtzüge Eira")
    mirna = sheet_cells(book, "Schlachtzüge Mirna")
    raids = sheet_cells(book, "Schlachtzüge")
    rr = find_row(mirna, 3, "Reinblütiger Feuerfalke")
    check("de: a Russian-named kill finds «Ragnaros» by encounter ID → «Ja»", rr is not None and mirna[rr][0] == "Ja", mirna.get(rr))
    check("de: new mount «Gesammelt?» = Ja (by mount ID)", raids[find_row(raids, 17, 1471)][0] == "Ja")
    check("de: …and its row left Eira's sheet", find_row(eira, 3, "Rache") is None)
    check("de: written values are German", not [t for w, t in all_texts(book) if CYR.search(t)])
    sd = de.sheet_data()
    check("de: in-game list: German sheet names, the boss under its Russian name too",
          "Schlachtzüge Mirna" in sd and "рагнарос" in sd and "Рейды" not in sd, sd[:600])

    # ---------------------------------------------------------------- week reset → «Nein»
    kills.append({"id": "R-1", "time": NOW - 5, "char": "Eira", "realm": "Blackhand", "charKey": "Player-Eira", "kind": "reset",
                  "boss": "Wochenreset", "weekEnd": WEEK_END})
    de.write_sv(db)
    code, out = de.run("--once")
    book = state()["spreadsheets"][book["id"]]
    mirna = sheet_cells(book, "Schlachtzüge Mirna")
    check("de: reset writes «Nein», not «Нет»", code == 0 and mirna[find_row(mirna, 3, "Reinblütiger Feuerfalke")][0] == "Nein", out[-800:])

    # ---------------------------------------------------------------- add a character
    db["chars"]["Kaelia-Blackhand"] = char("Kaelia", "Blackhand")
    de.write_sv(db)
    code, out = de.run("--add-chars")
    book = state()["spreadsheets"][book["id"]]
    titles = [x["title"] for x in book["sheets"]]
    check("de: --add-chars: German sheets next to the others", code == 0 and titles == ["Schlachtzüge", "Schlachtzüge Eira",
          "Schlachtzüge Mirna", "Schlachtzüge Kaelia", "Dungeons", "Dungeons Eira", "Dungeons Mirna", "Dungeons Kaelia", "Übersicht",
          "Weltbosse"], (titles, out[-800:]))
    kaelia = sheet_cells(book, "Schlachtzüge Kaelia")
    raids = sheet_cells(book, "Schlachtzüge")
    check("de: new character's sheet: German header, rows without collected mounts", kaelia[0][0] == "Diese Woche versucht?"
          and find_row(kaelia, 3, "Rache") is None and find_row(kaelia, 3, "Reinblütiger Feuerfalke") is not None
          and all(v[0] == "Nein" for k, v in kaelia.items() if k > 0), kaelia.get(0))
    check("de: «left» formula with three German sheets", raids[1][8].count("COUNTIFS") == 3 and "'Schlachtzüge Kaelia'" in raids[1][8],
          raids[1][8])
    world = sheet_cells(book, "Weltbosse")
    check("de: world bosses: a new column", world[0].get(14) == "Kaelia" and world[1].get(14) is False, world[0])
    check("de: still no Russian", not [t for w, t in all_texts(book) if CYR.search(t)],
          [t for w, t in all_texts(book) if CYR.search(t)][:10])
    code, out = de.run("--add-chars")
    check("de: --add-chars again: nothing to add (German sheets recognised)", code == 0 and "All characters are already in the spreadsheet" in out, out[-500:])
    code, out = de.run("--sync-collection")
    check("de: --sync-collection works on a German sheet", code == 0, out[-800:])

    # ================================================================ Russian game → English spreadsheet
    en = Setup("en", "ru")
    AYRA = {"name": "Эйра", "realm": "Ревущий фьорд", "class": "Охотница", "classFile": "HUNTER", "level": 90, "seen": NOW - 100}
    kills2 = []
    db2 = {"version": 1, "chars": {"Эйра-Ревущий фьорд": AYRA},
           "collection": {"updated": NOW, "locale": "ruRU", "journal": 1500, "collected": 1, "names": ["Пепел Ал'ара"], "ids": [183],
                          "mounts": [{"id": 183, "name": "Пепел Ал'ара", "type": 1, "got": True}]},
           "journal": {"locale": "ruRU", "build": "66192", "updated": NOW,
                       "instances": [{"id": 1193, "name": "Святилище Господства", "raid": True, "map": 2450}],
                       "encounters": [{"id": 2435, "j": 2441, "name": "Сильвана Ветрокрылая", "i": 1193}]},
           "kills": kills2}
    en.write_sv(db2)
    check("en: sign-in", en.login() == 0)
    before = set(state()["spreadsheets"])
    code, out = en.run("--create-sheet", "--sheet-lang", "en")
    check("en: --create-sheet --sheet-lang en exits 0", code == 0, out[-1500:])
    new = [i for i in state()["spreadsheets"] if i not in before]
    book = state()["spreadsheets"][new[0]]
    titles = [x["title"] for x in book["sheets"]]
    check("en: English sheet names, title, locale", titles == ["Raids", "Raids Эйра", "Dungeons", "Dungeons Эйра", "Summary",
          "World Bosses"] and book["title"] == "WoW Mounts" and book["locale"] == "en_US", (titles, book["title"], book["locale"]))
    raids = sheet_cells(book, "Raids")
    r = find_row(raids, 17, 1471)
    check("en: the Russian game's names are not used: English mount, boss, raid", r is not None and raids[r][3] == "Vengeance"
          and raids[r][5] == "Sylvanas Windrunner" and raids[r][2] == "Sanctum of Domination" and raids[r][0] == "No", raids.get(r))
    check("en: collected mount «Yes»", raids[find_row(raids, 17, 183)][0] == "Yes")
    check("en: formulas with commas", "," in raids[1][8] and "'Raids Эйра'" in raids[1][8] and '"No"' in raids[1][8], raids[1][8])
    russian = [(w, t) for w, t in all_texts(book) if CYR.search(t.replace("Эйра", ""))]
    check("en: no Russian apart from the character's name", not russian, russian[:20])
    kills2.append(kill("K-1", "Эйра", "Ревущий фьорд", "Сильвана Ветрокрылая", 2435, "Святилище Господства"))
    en.write_sv(db2)
    code, out = en.run("--once")
    book = state()["spreadsheets"][book["id"]]
    ayra = sheet_cells(book, "Raids Эйра")
    r = find_row(ayra, 3, "Vengeance")
    check("en: a Russian kill marks the English row «Yes»", code == 0 and r is not None and ayra[r][0] == "Yes", (ayra.get(r), out[-800:]))
    sd = en.sheet_data()
    check("en: in-game list carries the Russian boss name", "сильвана ветрокрылая" in sd and "Raids Эйра" in sd, sd[:600])

    # ================================================================ Russian spreadsheet: the game calls a boss differently
    ru = Setup("ru", "ru")
    kills3 = []
    db3 = {"version": 1, "chars": {"Эйра-Ревущий фьорд": AYRA},
           "collection": {"updated": NOW, "journal": 1500, "collected": 0, "names": []}, "kills": kills3}
    ru.write_sv(db3)
    check("ru: sign-in", ru.login() == 0)
    before = set(state()["spreadsheets"])
    code, out = ru.run("--create-sheet")   # no game language known: the program's (Russian)
    book = state()["spreadsheets"][[i for i in state()["spreadsheets"] if i not in before][0]]
    check("ru: default language is the program's: Russian sheets", code == 0 and book["sheets"][0]["title"] == "Рейды"
          and book["title"] == "Маунты WoW" and book["locale"] == "ru_RU", (out[-600:], book["title"]))
    # the game says «Галеон», the catalog «Галлеон»: the encounter ID decides
    kills3.append(kill("W-1", "Эйра", "Ревущий фьорд", "Галеон", 1563, "Пандария", diff=0, kind="world"))
    ru.write_sv(db3)
    code, out = ru.run("--once")
    book = state()["spreadsheets"][book["id"]]
    world = sheet_cells(book, "Мировые боссы")
    r = find_row(world, 4, "Галлеон")
    check("ru: «Галеон» from the game ticks «Галлеон» (encounter 1563)", code == 0 and r is not None and world[r].get(12) is True,
          (world.get(r), out[-800:]))
finally:
    mock.terminate()

failed = [n for n, ok in checks if not ok]
print("%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
