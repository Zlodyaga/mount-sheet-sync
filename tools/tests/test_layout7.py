"""2.10.2: spreadsheet layout 7 (C#, in-memory book and the sheet builder).

A new spreadsheet has the sheet «Выпавшие маунты»; an older one gets it on upgrade, and its Russian catalog texts that
were reworded are replaced — only cells that still hold the old text exactly. A mount record of the addon adds a row
to the sheet (date, character, mount, boss, attempts); a repeat does not add a second one; a sheet whose columns were
changed by hand is left alone.

  python3 test_layout7.py      (needs tools/cs_host/CollectionHost.exe and BuilderHost.exe)
"""
import json, os, subprocess, sys, tempfile, time
import paths

COLL = os.path.join(paths.HOSTS, "CollectionHost.exe")
BUILD = os.path.join(paths.HOSTS, "BuilderHost.exe")
TMP = tempfile.mkdtemp(prefix="mss-l7-")
checks = []


def check(name, cond, extra=None):
    checks.append((name, bool(cond)))
    print(("PASS " if cond else "FAIL ") + name + ("" if cond or extra is None else "\n     " + repr(extra)[:1500]))


def run(job):
    path = os.path.join(TMP, "job.json")
    json.dump(job, open(path, "w", encoding="utf-8"), ensure_ascii=False)
    p = subprocess.run(["mono", COLL, path], capture_output=True)
    if p.returncode != 0:
        print(p.stderr.decode("utf-8", "replace"))
    return json.loads(p.stdout.decode("utf-8"))


def sheet(out, name):
    found = [s for s in out["sheets"] if s["name"] == name]
    return found[0] if found else None


HEAD = ["Дата", "Маунт", "Босс / достижение / сундук", "Попыток", "Шанс за все попытки"]

# ---------------------------------------------------------------- a new spreadsheet
p = subprocess.run(["mono", BUILD], input=json.dumps({"title": "T", "chars": [{"name": "Эйра", "raids": True, "dungeons": True}]}).encode(),
                   capture_output=True)
reqs = json.loads(p.stdout.decode("utf-8"))["requests"]
added = [r["addSheet"]["properties"] for r in reqs if "addSheet" in r]
drops = [a for a in added if a["title"] == "Выпавшие маунты"]
check("a new spreadsheet has the sheet «Выпавшие маунты», the last one", len(drops) == 1 and drops[0]["index"] == max(a["index"] for a in added),
      [(a["title"], a["index"]) for a in added])
cells = [r["updateCells"] for r in reqs if "updateCells" in r and r["updateCells"].get("start", {}).get("sheetId") == (drops[0]["sheetId"] if drops else -1)]
head = [v["userEnteredValue"]["stringValue"] for v in cells[0]["rows"][0]["values"]] if cells else []
check("…with the header: date, mount, boss, attempts, the chance over all attempts", head == HEAD, head)

# ---------------------------------------------------------------- an older spreadsheet (layout 6) is upgraded
OLD_TIP = "Mythic-only; после TWW drop rate обычно понижается."
NEW_TIP = "Только эпохальный режим; после TWW шанс выпадения обычно понижают."
OLD_SKIP = "Quest: Up in Smoke; скипает боссов 3-6."
RH = ["Собрано?", "Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук", "Сложность / режим", "Сброс / частота",
      "Осталось попыток выбить", "Скип есть?", "Как работает скип", "Совет по фарму", "Статус", "Источник", "Шанс выпадения", "Попыток",
      "Шанс за все попытки", "ID в игре"]
CH = ["Пытался на этой неделе?", "Аддон", "Рейд", "Маунт", "Босс / достижение / сундук", "Сложность / режим", "Скип есть?", "Фармлю"]
SHEETS = [
    {"name": "Рейды", "rows": [RH,
        ["Нет", "SL", "Sanctum of Domination", "Отмщение", "Босс-дроп", "Сильвана Ветрокрылая", "Mythic", "Еженедельно", "", "Да", OLD_SKIP, OLD_TIP,
         "Фармится", "", "1%", 36, "", 1471],
        ["Нет", "SL", "Sepulcher", "Смотритель Зерета", "Босс-дроп", "Тюремщик", "Mythic", "Еженедельно", "", "Да", "Мой текст про скип",
         OLD_TIP + " (моя пометка)", "Фармится", "", "1%", 5, "", 1587]], "grid": 18},
    {"name": "Рейды Эйра", "rows": [CH, ["Нет", "SL", "Sanctum of Domination", "Отмщение", "Сильвана Ветрокрылая", "Mythic", "Да", True]], "grid": 8},
    {"name": "Сводка", "rows": [["Лист", "Всего"]]},
]
up = run({"catalog": None, "sheets": SHEETS, "mode": "upgrade", "properties": {"layout": "6"}})
check("layout 6 > 7 in one batch", up["changed"] is True and up["properties"].get("layout") == "7" and len(up["applied"]) == 1,
      (up.get("changed"), up.get("properties"), len(up.get("applied", []))))
d = sheet(up, "Выпавшие маунты")
check("…the table gets «Выпавшие маунты» with the header only, after every other sheet",
      d is not None and d["rows"] == [HEAD] and up["sheets"][-1]["name"] == "Выпавшие маунты", d and d["rows"])
batch = up["applied"][0] if up.get("applied") else []
new = [r["addSheet"]["properties"] for r in batch if "addSheet" in r]
check("…as one addSheet request with a sheet number of its own, at the end",
      [n["title"] for n in new] == ["Выпавшие маунты"] and new[0]["sheetId"] not in (100, 101, 102) and new[0]["index"] == 3, new)
raids = sheet(up, "Рейды")["rows"]
check("…reworded catalog texts are replaced where the cell still had the old text",
      raids[1][11] == NEW_TIP and raids[1][10] == "Задание «Up in Smoke»; пропускает боссов 3-6.", (raids[1][10], raids[1][11]))
check("…a text the player changed stays as it was", raids[2][11] == OLD_TIP + " (моя пометка)" and raids[2][10] == "Мой текст про скип", (raids[2][10], raids[2][11]))
again = run({"catalog": None, "sheets": up["sheets"], "mode": "upgrade", "properties": {"layout": "7"}})
check("layout 7: nothing more to do", again["changed"] is False)

# ---------------------------------------------------------------- a dropped mount adds a row
STAMP = int(time.mktime((2026, 11, 16, 12, 0, 0, 0, 0, -1)))
KILL = {"id": "M-1", "char": "Эйра", "realm": "Ревущий фьорд", "time": STAMP, "kind": "mount", "source": "mount",
        "boss": "Отмщение", "mount": "Отмщение", "mountID": 1471}


def handle(sheets, kills):
    return run({"catalog": None, "sheets": sheets, "mode": "handle", "properties": {"layout": "7"},
                "request": {"action": "kills", "client": "exe-test", "kills": kills}})


h = handle(up["sheets"], [KILL])
rows = sheet(h, "Выпавшие маунты")["rows"]
check("a mount record: «Собрано?» = «Да» as before", sheet(h, "Рейды")["rows"][1][0] == "Да", sheet(h, "Рейды")["rows"][1][:4])
check("…and a row on «Выпавшие маунты»: the date, the mount and the boss as the table names them, the attempts, the chance over them (1%, 36 > 30.4%)",
      len(rows) == 2 and rows[1][:4] == ["2026-11-16", "Отмщение", "Сильвана Ветрокрылая", 36] and abs(rows[1][4] - 0.3036) < 0.0001, rows)
h2 = handle(h["sheets"], [dict(KILL, id="M-2")])
check("the same mount again (a repeated record): no second row", len(sheet(h2, "Выпавшие маунты")["rows"]) == 2, sheet(h2, "Выпавшие маунты")["rows"])
h3 = handle(h2["sheets"], [dict(KILL, id="M-3", boss="Смотритель Зерета", mount="Смотритель Зерета", mountID=1587, char="Мирна", time=STAMP + 86400)])
rows = sheet(h3, "Выпавшие маунты")["rows"]
check("another mount: the next row", len(rows) == 3 and rows[2][:4] == ["2026-11-17", "Смотритель Зерета", "Тюремщик", 5] and abs(rows[2][4] - 0.049) < 0.0001, rows)

# the player changed the sheet's columns: the program does not write into them
changed = json.loads(json.dumps(up["sheets"]))
sheet({"sheets": changed}, "Выпавшие маунты")["rows"][0] = ["Когда", "Маунт", "Заметка", "Попыток", "Шанс"]
h4 = handle(changed, [KILL])
check("a sheet with other columns is left alone, the mount is still marked collected",
      len(sheet(h4, "Выпавшие маунты")["rows"]) == 1 and sheet(h4, "Рейды")["rows"][1][0] == "Да", sheet(h4, "Выпавшие маунты")["rows"])
# no such sheet at all (the player deleted it)
h5 = handle([s for s in up["sheets"] if s["name"] != "Выпавшие маунты"], [KILL])
check("no sheet «Выпавшие маунты»: nothing breaks, the mount is marked collected", sheet(h5, "Рейды")["rows"][1][0] == "Да" and sheet(h5, "Выпавшие маунты") is None)

# drops the addon remembered (db.drops, addon 3.0.7) before the sheet existed: added by the collection check
c = run({"catalog": None, "sheets": up["sheets"], "properties": {"layout": "7"},
         "game": {"ids": [1471, 1587], "names": [], "updated": STAMP, "dropped": {"1587": {"n": 12, "time": STAMP - 86400}, "1471": {"n": 36, "time": STAMP}, "999999": {"n": 3, "time": STAMP}}}})
rows = sheet(c, "Выпавшие маунты")["rows"]
check("drops the addon remembered go to the sheet, oldest first, named as the table names them now; a mount not in the table is skipped",
      c["result"]["dropsAdded"] == 2 and [r[1] for r in rows[1:]] == [sheet(c, "Рейды")["rows"][2][3], "Отмщение"] and rows[1][3] == 12 and rows[1][0] == "2026-11-15", rows)
c2 = run({"catalog": None, "sheets": c["sheets"], "properties": {"layout": "7"},
          "game": {"ids": [1471, 1587], "names": [], "updated": STAMP + 60, "dropped": {"1587": {"n": 12, "time": STAMP - 86400}}}})
check("…and not twice", c2["result"]["dropsAdded"] == 0 and len(sheet(c2, "Выпавшие маунты")["rows"]) == 3)

failed = [n for n, ok in checks if not ok]
print("\n%d/%d checks passed" % (len(checks) - len(failed), len(checks)))
sys.exit(1 if failed else 0)
